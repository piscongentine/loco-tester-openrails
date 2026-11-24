// Loco Tester - WPF 3D viewer: orbit / pan / zoom camera, cab POV, animated wheels,
// pantographs, wipers, doors, steam rods, and the loco's own lights.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace LocoTester
{
    /// <summary>Frozen geometry built on a worker thread, ready to show.</summary>
    public class BuiltModel
    {
        public ShapeModel Shape;
        public List<KeyValuePair<int, GeometryModel3D>> Opaque = new List<KeyValuePair<int, GeometryModel3D>>();
        public List<KeyValuePair<int, GeometryModel3D>> Alpha = new List<KeyValuePair<int, GeometryModel3D>>();
        public int Textures, MissingTextures;
        public long BuildMs;
    }

    public class AnimState
    {
        public double WheelAngle;      // radians
        public double Panto1, Panto2;  // 0 down .. 1 up
        public double Wiper;           // 0..1 sweep position
        public double Doors;           // 0..1
        public double Mirrors;         // 0..1
        public int Headlight;          // 0 off, 1 dim, 2 bright
        public bool RearUnit;
        public double Time;            // seconds, for blinking lights
        public double Throttle;        // 0..1   (smoke density)
        public double Speed;           // m/s    (smoke trails backwards)
        public bool Power = true;      // engine running
        public bool Horn;              // steam whistle plume
        public bool Smoke = true;      // smoke effect switched on
        public bool Penalty;           // emergency / penalty brake applied (penalty lights)
        public bool Rain;              // weather condition for lights (wipers on = rain)
        public int Reverser = 1;       // 1 forward, 0 neutral, -1 reverse
    }

    public class LocoViewer
    {
        readonly Border host;
        readonly Viewport3D vp = new Viewport3D();
        readonly PerspectiveCamera cam = new PerspectiveCamera();
        readonly Model3DGroup root = new Model3DGroup();
        readonly Model3DGroup modelGroup = new Model3DGroup();
        readonly Model3DGroup lightGroup = new Model3DGroup();
        readonly AmbientLight ambient = new AmbientLight(Color.FromRgb(90, 96, 110));
        readonly DirectionalLight sun = new DirectionalLight(Color.FromRgb(230, 226, 215), new Vector3D(-0.4, -0.8, -0.5));
        readonly DirectionalLight fill = new DirectionalLight(Color.FromRgb(70, 85, 110), new Vector3D(0.6, -0.3, 0.7));
        MatrixTransform3D[] transforms = new MatrixTransform3D[0];
        BuiltModel model;
        Loco loco;
        CabFile cab;

        // camera
        Point3D target = new Point3D(0, 2, 0);
        double yaw = 145, pitch = 15, dist = 25, fitDist = 25;
        bool pov; double povYaw, povPitch; Point3D povPos;
        Point lastMouse; bool dragging, panning;

        public bool Night;
        public bool Turntable;
        string lightKey = "";
        readonly SmokeSystem smoke = new SmokeSystem();
        readonly Model3DGroup envGroup = new Model3DGroup();     // shed / open track
        readonly Model3DGroup roofGroup = new Model3DGroup();    // shed roof, hidden when the camera is above it
        Model3D roofModel;
        readonly List<PointLight> shedLights = new List<PointLight>();
        Model3DGroup pools;                                      // warm light pools on the floor (night)
        readonly Model3DGroup poolGroup = new Model3DGroup();
        bool nightShown;
        // pantograph sparks
        readonly Model3DGroup sparkGroup = new Model3DGroup();
        readonly Point3D?[] pantoTop = new Point3D?[3];
        readonly double[] sparkLeft = new double[3], sparkNext = new double[3];
        readonly Random rnd = new Random();
        public string Environment = "shed";                      // "shed" or "open"
        string envKey = "";
        static readonly MeshGeometry3D sphere = MakeSphere(14, 10);
        static readonly MeshGeometry3D cone = MakeCone(18);

        public LocoViewer(Border host)
        {
            this.host = host;
            cam.FieldOfView = 45; cam.NearPlaneDistance = 0.05; cam.FarPlaneDistance = 2000;
            vp.Camera = cam;
            root.Children.Add(ambient); root.Children.Add(sun); root.Children.Add(fill);
            root.Children.Add(modelGroup);
            root.Children.Add(lightGroup);
            root.Children.Add(envGroup);
            root.Children.Add(roofGroup);
            root.Children.Add(smoke.Group);   // translucent: drawn after the model and the shed
            root.Children.Add(poolGroup);
            root.Children.Add(lightsR.Group);
            root.Children.Add(sparkGroup);
            ModelVisual3D mv = new ModelVisual3D(); mv.Content = root;
            vp.Children.Add(mv);
            RenderOptions.SetEdgeMode(vp, EdgeMode.Unspecified);
            host.Child = vp;
            host.MouseDown += OnDown; host.MouseUp += OnUp; host.MouseMove += OnMove; host.MouseWheel += OnWheel;
            UpdateCamera();
        }

        public BuiltModel Model { get { return model; } }

        // ---------------------------------------------------------------- building

        public static BuiltModel Build(Loco l, CancellationToken ct)
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            BuiltModel b = new BuiltModel();
            b.Shape = ShapeReader.Load(l.ShapePath);
            if (b.Shape.Error != null) return b;
            Dictionary<string, Material> mats = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            // merge meshes with the same node + texture + alpha flag
            Dictionary<string, List<ShapeMesh>> groups = new Dictionary<string, List<ShapeMesh>>();
            List<string> order = new List<string>();
            foreach (ShapeMesh m in b.Shape.Meshes)
            {
                string key = m.Matrix + "|" + (m.Texture ?? "") + "|" + m.Alpha;
                List<ShapeMesh> g;
                if (!groups.TryGetValue(key, out g)) { g = new List<ShapeMesh>(); groups[key] = g; order.Add(key); }
                g.Add(m);
            }
            foreach (string key in order)
            {
                ct.ThrowIfCancellationRequested();
                List<ShapeMesh> g = groups[key];
                MeshGeometry3D mesh = new MeshGeometry3D();
                Point3DCollection pos = new Point3DCollection(); Vector3DCollection nrm = new Vector3DCollection();
                PointCollection uv = new PointCollection(); Int32Collection idx = new Int32Collection();
                foreach (ShapeMesh m in g)
                {
                    int baseIdx = pos.Count;
                    foreach (Point3D p in m.Positions) pos.Add(p);
                    foreach (Vector3D n in m.Normals) nrm.Add(n);
                    foreach (Point p in m.UVs) uv.Add(p);
                    foreach (int i in m.Indices) idx.Add(baseIdx + i);
                }
                mesh.Positions = pos; mesh.Normals = nrm; mesh.TextureCoordinates = uv; mesh.TriangleIndices = idx;
                mesh.Freeze();
                ShapeMesh first = g[0];
                Material mat;
                string tex = first.Texture ?? "";
                if (!mats.TryGetValue(tex, out mat))
                {
                    mat = MakeMaterial(l.Folder, first.Texture, ref b.Textures, ref b.MissingTextures);
                    mats[tex] = mat;
                }
                GeometryModel3D gm = new GeometryModel3D(mesh, mat);
                gm.BackMaterial = mat;
                gm.Freeze();
                (first.Alpha ? b.Alpha : b.Opaque).Add(new KeyValuePair<int, GeometryModel3D>(first.Matrix, gm));
            }
            b.BuildMs = sw.ElapsedMilliseconds;
            return b;
        }

        /// <summary>Full-resolution textures with high-quality filtering (HD); off = 1024 px, faster on weak PCs.</summary>
        public static bool HighDetail = true;

        /// <summary>WPF render tier: 2 = full hardware 3D, 1 = partial, 0 = software only.</summary>
        public static int RenderTier { get { return RenderCapability.Tier >> 16; } }

        internal static Material MakeMaterial(string folder, string tex, ref int ok, ref int missing)
        {
            BitmapSource bmp = null;
            if (!string.IsNullOrEmpty(tex))
            {
                string p = Path.Combine(folder, tex);
                if (!File.Exists(p))
                {
                    // MSTS also looks one level up (shared textures) and in a TEXTURES subfolder
                    string up = Path.Combine(Path.GetDirectoryName(folder) ?? folder, tex);
                    p = File.Exists(up) ? up : null;
                }
                if (p != null) bmp = AceReader.LoadCached(p, HighDetail ? 4096 : 1024);
                if (bmp != null) ok++; else missing++;
            }
            Material m;
            if (bmp != null)
            {
                ImageBrush br = new ImageBrush(bmp);
                br.ViewportUnits = BrushMappingMode.Absolute;
                br.Viewport = new Rect(0, 0, 1, 1);
                br.TileMode = TileMode.Tile;
                RenderOptions.SetBitmapScalingMode(br, HighDetail ? BitmapScalingMode.HighQuality : BitmapScalingMode.Linear);
                RenderOptions.SetCachingHint(br, CachingHint.Cache);
                br.Freeze();
                m = new DiffuseMaterial(br);
            }
            else m = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(150, 156, 168)));
            m.Freeze();
            return m;
        }

        public void Show(Loco l, BuiltModel b, CabFile cabFile)
        {
            loco = l; model = b; cab = cabFile;
            smoke.Reset();
            modelGroup.Children.Clear();
            lightGroup.Children.Clear();
            lightKey = "";
            if (b == null || b.Shape == null || b.Shape.Error != null) { transforms = new MatrixTransform3D[0]; return; }
            int n = Math.Max(1, b.Shape.Matrices.Count);
            transforms = new MatrixTransform3D[n];
            Matrix3D[] world = ShapeReader.World(b.Shape, null);
            for (int i = 0; i < n; i++) transforms[i] = new MatrixTransform3D(i < world.Length ? world[i] : Matrix3D.Identity);
            Model3DGroup[] op = new Model3DGroup[n], al = new Model3DGroup[n];
            foreach (KeyValuePair<int, GeometryModel3D> kv in b.Opaque) Add(op, kv);
            foreach (KeyValuePair<int, GeometryModel3D> kv in b.Alpha) Add(al, kv);
            foreach (Model3DGroup g in op) if (g != null) modelGroup.Children.Add(g);
            foreach (Model3DGroup g in al) if (g != null) modelGroup.Children.Add(g);
            BuildEnvironment(true);
            Rect3D bb = b.Shape.Bounds;
            if (!bb.IsEmpty)
            {
                target = new Point3D(bb.X + bb.SizeX / 2, bb.Y + bb.SizeY / 2, bb.Z + bb.SizeZ / 2);
                double size = Math.Max(bb.SizeX, Math.Max(bb.SizeY, bb.SizeZ));
                fitDist = Math.Max(4, size * 1.6);
            }
            Preset("3/4");
        }

        void Add(Model3DGroup[] arr, KeyValuePair<int, GeometryModel3D> kv)
        {
            int i = Math.Max(0, Math.Min(arr.Length - 1, kv.Key));
            if (arr[i] == null) { arr[i] = new Model3DGroup(); arr[i].Transform = transforms[i]; }
            arr[i].Children.Add(kv.Value);
        }

        public void Clear()
        {
            model = null;
            modelGroup.Children.Clear();
            lightGroup.Children.Clear();
            transforms = new MatrixTransform3D[0];
        }

        // ---------------------------------------------------------------- animation

        /// <summary>Applies wheel / pantograph / wiper / door animation and the light state.</summary>
        public void Apply(AnimState s, double dt)
        {
            if (Turntable && !pov) { yaw = (yaw + dt * 18) % 360; UpdateCamera(); }
            if (model == null || model.Shape == null || transforms.Length == 0) return;
            ShapeModel sh = model.Shape;
            int n = sh.Matrices.Count;
            Matrix3D[] locals = new Matrix3D[n];
            bool any = false;
            double rev = (s.WheelAngle / (2 * Math.PI)) % 1.0; if (rev < 0) rev += 1;
            for (int i = 0; i < n; i++)
            {
                ShapeMatrix m = sh.Matrices[i];
                string name = (m.Name ?? "").ToUpperInvariant();
                Matrix3D local = m.Local;
                if (name.StartsWith("WHEELS") && m.Animated)
                {
                    // a WHEELS part with its own keyframes (radiator fans, rods...): play its keys once per
                    // wheel turn, the way Open Rails does, so it spins about its own axis
                    local = sh.LocalAt(i, rev * m.MaxFrame); any = true;
                }
                else if (name.StartsWith("WHEELS") || (name.Contains("WHEEL") && !name.Contains("BOGIE") && !m.Animated))
                {
                    Matrix3D r = Matrix3D.Identity;
                    r.Rotate(new Quaternion(new Vector3D(1, 0, 0), -s.WheelAngle * 180 / Math.PI));
                    local = r * m.Local; any = true;
                }
                else if (m.Animated)
                {
                    double max = m.MaxFrame;
                    if (name.Contains("PANTO")) { double st = name.EndsWith("2") || name.Contains("2A") || name.Contains("2B") ? s.Panto2 : s.Panto1; local = sh.LocalAt(i, st * max); any = true; }
                    else if (name.Contains("WIPER")) { local = sh.LocalAt(i, s.Wiper * max); any = true; }
                    else if (name.Contains("MIRROR")) { local = sh.LocalAt(i, s.Mirrors * max); any = true; }
                    else if (name.Contains("DOOR")) { local = sh.LocalAt(i, s.Doors * max); any = true; }
                    else { local = sh.LocalAt(i, rev * max); any = true; }   // other running gear: follows the wheels
                }
                locals[i] = local;
            }
            if (any)
            {
                Matrix3D[] world = ShapeReader.World(sh, locals);
                for (int i = 0; i < n && i < transforms.Length; i++) transforms[i].Matrix = world[i];
            }
            UpdateLights(s);
            smoke.Update(loco, s, dt, cam.Position, cam.LookDirection, cam.UpDirection);
            UpdateSparks(s, dt, locals);
        }

        /// <summary>What the model can animate, for the info line.</summary>
        public string Capabilities()
        {
            if (model == null || model.Shape == null) return "";
            int wheels = 0, gear = 0, panto = 0, wipers = 0, doors = 0, mirrors = 0;
            foreach (ShapeMatrix m in model.Shape.Matrices)
            {
                string n = (m.Name ?? "").ToUpperInvariant();
                if (n.StartsWith("WHEELS") && !m.Animated) wheels++;
                else if (n.Contains("PANTO") && m.Animated) panto++;
                else if (n.Contains("WIPER") && m.Animated) wipers++;
                else if (n.Contains("MIRROR") && m.Animated) mirrors++;
                else if (n.Contains("DOOR") && m.Animated) doors++;
                else if (m.Animated) gear++;
            }
            int smokeOut = loco != null ? loco.Emitters.Count : 0;
            return "wheel sets " + wheels + " · fans / running gear " + gear + " · pantograph parts " + panto + " · wipers " + wipers + " · doors " + doors + " · mirrors " + mirrors + " · smoke outlets " + smokeOut;
        }

        readonly LightRenderer lightsR = new LightRenderer();

        void UpdateLights(AnimState s)
        {
            // lighting of the scene: dim at night, the shed lamps stay on
            ambient.Color = Night ? Color.FromRgb(22, 26, 36) : Color.FromRgb(90, 96, 110);
            sun.Color = Night ? Color.FromRgb(40, 46, 66) : Color.FromRgb(230, 226, 215);
            fill.Color = Night ? Color.FromRgb(16, 20, 30) : Color.FromRgb(70, 85, 110);
            lightsR.Update(loco, s, Night, cam.Position, cam.LookDirection, cam.UpDirection);
            if (Night != nightShown)
            {
                nightShown = Night;
                // warm sodium-style shed lamps at night, softer white by day
                foreach (PointLight pl in shedLights) { pl.Color = Night ? Color.FromRgb(255, 178, 96) : Color.FromRgb(120, 116, 104); pl.Range = Night ? 16 : 18; pl.LinearAttenuation = Night ? 0.11 : 0.05; }
                poolGroup.Children.Clear();
                if (Night && pools != null) poolGroup.Children.Add(pools);
                RadialGradientBrush sky = new RadialGradientBrush { Center = new Point(0.5, 0.25), RadiusX = 0.9, RadiusY = 0.9 };
                sky.GradientStops.Add(new GradientStop(Night ? Color.FromRgb(16, 24, 44) : Color.FromRgb(27, 42, 68), 0));
                sky.GradientStops.Add(new GradientStop(Night ? Color.FromRgb(3, 5, 10) : Color.FromRgb(10, 17, 29), 1));
                // paint the 3D page frame (the first Border above the view host)
                for (DependencyObject d = host.Parent; d != null; d = LogicalTreeHelper.GetParent(d))
                {
                    Border frame = d as Border;
                    if (frame != null) { frame.Background = sky; break; }
                }
            }
        }

        public int LightsOn { get { return lightsR.Shown; } }

        /// <summary>Contact point of a raised pantograph: the highest point of its parts.</summary>
        Point3D? FindPantoTop(int group, Matrix3D[] locals)
        {
            if (model == null) return null;
            ShapeModel sh = model.Shape;
            Matrix3D[] world = ShapeReader.World(sh, locals);
            double best = double.MinValue; Point3D bp = new Point3D();
            foreach (ShapeMesh me in sh.Meshes)
            {
                string n = (sh.Matrices[me.Matrix].Name ?? "").ToUpperInvariant();
                if (!n.Contains("PANTO")) continue;
                bool two = n.EndsWith("2") || n.Contains("2A") || n.Contains("2B");
                if ((group == 2) != two) continue;
                foreach (Point3D p in me.Positions) { Point3D q = world[me.Matrix].Transform(p); if (q.Y > best) { best = q.Y; bp = q; } }
            }
            if (best == double.MinValue) return null;
            return bp;
        }

        /// <summary>Blue-white arcing at the contact point of each raised pantograph; more often at speed.</summary>
        void UpdateSparks(AnimState s, double dt, Matrix3D[] locals)
        {
            sparkGroup.Children.Clear();
            for (int g = 1; g <= 2; g++)
            {
                double st = g == 1 ? s.Panto1 : s.Panto2;
                if (st < 0.98) { pantoTop[g] = null; sparkLeft[g] = 0; continue; }
                if (pantoTop[g] == null)
                {
                    pantoTop[g] = FindPantoTop(g, locals);
                    sparkLeft[g] = 0.25;                 // arc as the head touches the wire
                    sparkNext[g] = 1 + rnd.NextDouble() * 2;
                }
                if (pantoTop[g] == null) continue;
                sparkNext[g] -= dt * (0.4 + Math.Abs(s.Speed) / 6);
                if (sparkNext[g] <= 0 && Math.Abs(s.Speed) > 0.5) { sparkLeft[g] = 0.05 + rnd.NextDouble() * 0.12; sparkNext[g] = 0.6 + rnd.NextDouble() * 3; }
                if (sparkLeft[g] <= 0) continue;
                sparkLeft[g] -= dt;
                Point3D c = pantoTop[g].Value + new Vector3D((rnd.NextDouble() - 0.5) * 0.3, 0.03, (rnd.NextDouble() - 0.5) * 0.2);
                double size = 0.25 + rnd.NextDouble() * 0.45;
                Vector3D look = cam.LookDirection, up = cam.UpDirection;
                Vector3D right = Vector3D.CrossProduct(look, up); right.Normalize();
                Vector3D upv = Vector3D.CrossProduct(right, look); upv.Normalize();
                MeshGeometry3D m = new MeshGeometry3D();
                foreach (double sc in new[] { size, size * 0.45 })
                {
                    int b = m.Positions.Count;
                    m.Positions.Add(c - right * sc - upv * sc); m.Positions.Add(c + right * sc - upv * sc); m.Positions.Add(c + right * sc + upv * sc); m.Positions.Add(c - right * sc + upv * sc);
                    m.TextureCoordinates.Add(new Point(0, 1)); m.TextureCoordinates.Add(new Point(1, 1)); m.TextureCoordinates.Add(new Point(1, 0)); m.TextureCoordinates.Add(new Point(0, 0));
                    m.TriangleIndices.Add(b); m.TriangleIndices.Add(b + 1); m.TriangleIndices.Add(b + 2); m.TriangleIndices.Add(b); m.TriangleIndices.Add(b + 2); m.TriangleIndices.Add(b + 3);
                }
                // a few short jagged sparks shooting off
                for (int k = 0; k < 4; k++)
                {
                    Vector3D d = new Vector3D(rnd.NextDouble() - 0.5, rnd.NextDouble() * 0.6 - 0.1, rnd.NextDouble() - 0.5); d.Normalize();
                    Point3D e = c + d * (0.2 + rnd.NextDouble() * 0.5);
                    Vector3D side = Vector3D.CrossProduct(d, look); if (side.Length < 1e-6) continue; side.Normalize(); side *= 0.012;
                    int b = m.Positions.Count;
                    m.Positions.Add(c - side); m.Positions.Add(c + side); m.Positions.Add(e + side); m.Positions.Add(e - side);
                    for (int q = 0; q < 4; q++) m.TextureCoordinates.Add(new Point(0.5, 0.5));
                    m.TriangleIndices.Add(b); m.TriangleIndices.Add(b + 1); m.TriangleIndices.Add(b + 2); m.TriangleIndices.Add(b); m.TriangleIndices.Add(b + 2); m.TriangleIndices.Add(b + 3);
                }
                if (sparkMat == null)
                {
                    ImageBrush br = new ImageBrush(LightRenderer.flareTex) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) };
                    MaterialGroup mg = new MaterialGroup();
                    mg.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))));
                    EmissiveMaterial em = new EmissiveMaterial(br); em.Color = Color.FromRgb(190, 220, 255);
                    mg.Children.Add(em); mg.Freeze();
                    sparkMat = mg;
                }
                GeometryModel3D gm = new GeometryModel3D(m, sparkMat); gm.BackMaterial = sparkMat;
                sparkGroup.Children.Add(gm);
                PointLight flash = new PointLight(Color.FromRgb(170, 200, 255), c);
                flash.Range = Night ? 14 : 6; flash.ConstantAttenuation = 1; flash.LinearAttenuation = 0.25;
                sparkGroup.Children.Add(flash);
            }
        }

        Material sparkMat;

        static Model3D Glow(Point3D p, double r, Color c, double opacity)
        {
            MaterialGroup mg = new MaterialGroup();
            mg.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), 0, 0, 0))));
            mg.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B))));
            GeometryModel3D g = new GeometryModel3D(sphere, mg);
            Transform3DGroup t = new Transform3DGroup();
            t.Children.Add(new ScaleTransform3D(r, r, r));
            t.Children.Add(new TranslateTransform3D(p.X, p.Y, p.Z));
            g.Transform = t;
            return g;
        }

        static Model3D Beam(Point3D p, double len, bool back, Color c, double opacity)
        {
            MaterialGroup mg = new MaterialGroup();
            mg.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))));
            mg.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B))));
            GeometryModel3D g = new GeometryModel3D(cone, mg);
            g.BackMaterial = mg;
            Transform3DGroup t = new Transform3DGroup();
            t.Children.Add(new ScaleTransform3D(len * 0.18, len * 0.12, len * (back ? 1 : -1)));
            t.Children.Add(new TranslateTransform3D(p.X, p.Y, p.Z));
            g.Transform = t;
            return g;
        }

        /// <summary>Builds the workshop or the open track around the loco (rails at the loco's own gauge).</summary>
        public void BuildEnvironment(bool force)
        {
            bool electric = loco != null && (loco.EngineType ?? "").IndexOf("Electric", StringComparison.OrdinalIgnoreCase) >= 0;
            double rc = Scenery.RailCentre(model != null ? model.Shape : null);
            string key = Environment + "|" + electric + "|" + Math.Round(rc, 3) + "|" + (model != null ? model.GetHashCode() : 0);
            if (key == envKey) return;
            envKey = key;
            // Open Rails root (the folder that holds TRAINS) for the DB track and its textures
            string orRoot = null;
            if (loco != null)
                for (string d = loco.Folder; d != null; d = System.IO.Path.GetDirectoryName(d))
                    if (string.Equals(System.IO.Path.GetFileName(d), "TRAINS", StringComparison.OrdinalIgnoreCase)) { orRoot = System.IO.Path.GetDirectoryName(d); break; }
            Model3DGroup db = Scenery.DbTrack(orRoot, rc);
            // contact wire exactly at the height a raised pantograph reaches
            double wireH = 5.55;
            if (electric && model != null)
            {
                ShapeModel sh = model.Shape;
                Matrix3D[] up = new Matrix3D[sh.Matrices.Count];
                for (int i = 0; i < up.Length; i++) up[i] = sh.Matrices[i].Animated && (sh.Matrices[i].Name ?? "").ToUpperInvariant().Contains("PANTO") ? sh.LocalAt(i, sh.Matrices[i].MaxFrame) : sh.Matrices[i].Local;
                Point3D? top = FindPantoTop(1, up) ?? FindPantoTop(2, up);
                if (top.HasValue && top.Value.Y > 4 && top.Value.Y < 8) wireH = top.Value.Y + 0.005;
            }
            envGroup.Children.Clear(); roofGroup.Children.Clear(); shedLights.Clear(); roofModel = null; pools = null; poolGroup.Children.Clear(); nightShown = false;
            if (Environment == "open")
            {
                envGroup.Children.Add(Scenery.OpenTrack(model != null ? model.Shape : null, db));
            }
            else
            {
                List<Point3D> lamps;
                Model3DGroup roof;
                envGroup.Children.Add(Scenery.Shed(model != null ? model.Shape : null, electric, wireH, db != null, out lamps, out roof, out pools));
                if (db != null) envGroup.Children.Add(db);
                roofModel = roof;
                roofGroup.Children.Add(roof);
                // a few of the shed lamps light the loco (WPF has a light budget, so only the nearest)
                foreach (Point3D p in lamps)
                    if (Math.Abs(p.Z) < 14)
                    {
                        PointLight pl = new PointLight(Color.FromRgb(120, 116, 104), p);
                        pl.Range = 18; pl.ConstantAttenuation = 1; pl.LinearAttenuation = 0.05;
                        shedLights.Add(pl);
                        envGroup.Children.Add(pl);
                    }
            }
        }

        void UpdateRoof()
        {
            // cut-away: hide the roof when the camera is above it, so top views still show the loco
            if (roofModel == null) return;
            bool show = cam.Position.Y < 9.5 || pov;
            if (show && roofGroup.Children.Count == 0) roofGroup.Children.Add(roofModel);
            else if (!show && roofGroup.Children.Count > 0) roofGroup.Children.Clear();
        }

        static Model3D Ground()
        {
            MeshGeometry3D m = new MeshGeometry3D();
            double s = 60;
            m.Positions = new Point3DCollection(new Point3D[] { new Point3D(-s, 0, -s), new Point3D(s, 0, -s), new Point3D(s, 0, s), new Point3D(-s, 0, s) });
            m.TriangleIndices = new Int32Collection(new int[] { 0, 2, 1, 0, 3, 2 });
            RadialGradientBrush br = new RadialGradientBrush(Color.FromArgb(120, 40, 60, 90), Color.FromArgb(0, 10, 17, 29));
            m.TextureCoordinates = new PointCollection(new Point[] { new Point(0, 0), new Point(1, 0), new Point(1, 1), new Point(0, 1) });
            DiffuseMaterial mat = new DiffuseMaterial(br);
            GeometryModel3D g = new GeometryModel3D(m, mat);
            g.BackMaterial = mat;
            // rails
            Model3DGroup grp = new Model3DGroup();
            grp.Children.Add(g);
            foreach (double x in new double[] { -0.7175, 0.7175 })
            {
                MeshGeometry3D r = Box(x - 0.04, 0.0, -s, x + 0.04, 0.16, s);
                grp.Children.Add(new GeometryModel3D(r, new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(120, 128, 140)))));
            }
            return grp;
        }

        static MeshGeometry3D Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            MeshGeometry3D m = new MeshGeometry3D();
            Point3D[] p = { new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), new Point3D(x1, y1, z0), new Point3D(x0, y1, z0),
                            new Point3D(x0, y0, z1), new Point3D(x1, y0, z1), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1) };
            int[] f = { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 3, 6, 2, 3, 7, 6, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5 };
            m.Positions = new Point3DCollection(p); m.TriangleIndices = new Int32Collection(f);
            m.Freeze();
            return m;
        }

        static MeshGeometry3D MakeSphere(int seg, int rings)
        {
            MeshGeometry3D m = new MeshGeometry3D();
            for (int r = 0; r <= rings; r++)
            {
                double phi = Math.PI * r / rings;
                for (int s = 0; s <= seg; s++)
                {
                    double th = 2 * Math.PI * s / seg;
                    Vector3D v = new Vector3D(Math.Sin(phi) * Math.Cos(th), Math.Cos(phi), Math.Sin(phi) * Math.Sin(th));
                    m.Positions.Add((Point3D)v); m.Normals.Add(v);
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + seg + 1;
                    m.TriangleIndices.Add(a); m.TriangleIndices.Add(a + 1); m.TriangleIndices.Add(b);
                    m.TriangleIndices.Add(a + 1); m.TriangleIndices.Add(b + 1); m.TriangleIndices.Add(b);
                }
            m.Freeze();
            return m;
        }

        static MeshGeometry3D MakeCone(int seg)
        {
            // apex at origin, opening along +Z, unit length, unit radius
            MeshGeometry3D m = new MeshGeometry3D();
            m.Positions.Add(new Point3D(0, 0, 0));
            for (int s = 0; s <= seg; s++) { double th = 2 * Math.PI * s / seg; m.Positions.Add(new Point3D(Math.Cos(th), Math.Sin(th), 1)); }
            for (int s = 1; s <= seg; s++) { m.TriangleIndices.Add(0); m.TriangleIndices.Add(s); m.TriangleIndices.Add(s + 1); }
            m.Freeze();
            return m;
        }

        // ---------------------------------------------------------------- camera

        public void Preset(string name)
        {
            pov = false;
            cam.FieldOfView = 45;
            dist = fitDist;
            switch (name)
            {
                case "front": yaw = 180; pitch = 6; break;
                case "rear": yaw = 0; pitch = 6; break;
                case "side": yaw = 90; pitch = 4; break;
                case "top": yaw = 90; pitch = 85; break;
                default: yaw = 145; pitch = 16; break;
            }
            UpdateCamera();
        }

        /// <summary>Driver's eye position, taken from the 2D cab file (Position x y z).</summary>
        public bool CabPov()
        {
            double x = -0.75, y = 3, z = 8;
            if (cab != null && cab.Views.Count > 0) { x = cab.Views[0].X; y = cab.Views[0].Y; z = cab.Views[0].Z; }
            else if (model != null && !model.Shape.Bounds.IsEmpty) { Rect3D b = model.Shape.Bounds; x = -0.6; y = b.Y + b.SizeY * 0.72; z = -(b.Z) - 1.2; }
            povPos = new Point3D(x, y, -z);
            povYaw = 0; povPitch = -6;
            pov = true;
            cam.FieldOfView = 70;
            UpdateCamera();
            return cab != null && cab.Views.Count > 0;
        }

        void UpdateCamera()
        {
            if (pov)
            {
                double yr = povYaw * Math.PI / 180, pr = povPitch * Math.PI / 180;
                Vector3D look = new Vector3D(Math.Sin(yr) * Math.Cos(pr), Math.Sin(pr), -Math.Cos(yr) * Math.Cos(pr));
                cam.Position = povPos; cam.LookDirection = look; cam.UpDirection = new Vector3D(0, 1, 0);
                UpdateRoof();
                return;
            }
            double y = yaw * Math.PI / 180, p = pitch * Math.PI / 180;
            Vector3D off = new Vector3D(Math.Sin(y) * Math.Cos(p), Math.Sin(p), Math.Cos(y) * Math.Cos(p)) * dist;
            cam.Position = target + off;
            cam.LookDirection = -off;
            cam.UpDirection = new Vector3D(0, 1, 0);
            UpdateRoof();
        }

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left) { Preset("3/4"); return; }
            lastMouse = e.GetPosition(host);
            dragging = e.ChangedButton == MouseButton.Left;
            panning = e.ChangedButton == MouseButton.Right || e.ChangedButton == MouseButton.Middle;
            host.CaptureMouse();
        }

        void OnUp(object sender, MouseButtonEventArgs e) { dragging = panning = false; host.ReleaseMouseCapture(); }

        void OnMove(object sender, MouseEventArgs e)
        {
            if (!dragging && !panning) return;
            Point p = e.GetPosition(host);
            Vector d = p - lastMouse; lastMouse = p;
            if (pov)
            {
                povYaw += d.X * 0.25; povPitch = Math.Max(-80, Math.Min(80, povPitch - d.Y * 0.25));
            }
            else if (dragging)
            {
                yaw -= d.X * 0.4; pitch = Math.Max(-10, Math.Min(89, pitch + d.Y * 0.3));
            }
            else
            {
                Vector3D right = Vector3D.CrossProduct(cam.LookDirection, cam.UpDirection); right.Normalize();
                Vector3D up = Vector3D.CrossProduct(right, cam.LookDirection); up.Normalize();
                double k = dist * 0.0016;
                target += -right * d.X * k + up * d.Y * k;
            }
            UpdateCamera();
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (pov) { cam.FieldOfView = Math.Max(20, Math.Min(100, cam.FieldOfView - e.Delta * 0.03)); return; }
            dist = Math.Max(1.5, Math.Min(400, dist * (e.Delta > 0 ? 0.88 : 1.14)));
            UpdateCamera();
        }
    }
}
