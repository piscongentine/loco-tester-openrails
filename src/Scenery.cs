// Loco Tester - procedural 3D surroundings: a loco shed (workshop) with an inspection pit, or open track.
// Everything is generated in code (meshes + textures), nothing is loaded from disk.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace LocoTester
{
    /// <summary>Collects boxes / quads into one mesh (one draw call per material).</summary>
    class MeshBuilder
    {
        public readonly Point3DCollection P = new Point3DCollection();
        public readonly Vector3DCollection N = new Vector3DCollection();
        public readonly PointCollection T = new PointCollection();
        public readonly Int32Collection I = new Int32Collection();

        public void Quad(Point3D a, Point3D b, Point3D c, Point3D d, double u, double v)
        {
            Vector3D n = Vector3D.CrossProduct(b - a, d - a); if (n.Length > 0) n.Normalize();
            int k = P.Count;
            P.Add(a); P.Add(b); P.Add(c); P.Add(d);
            for (int i = 0; i < 4; i++) N.Add(n);
            T.Add(new Point(0, v)); T.Add(new Point(u, v)); T.Add(new Point(u, 0)); T.Add(new Point(0, 0));
            I.Add(k); I.Add(k + 1); I.Add(k + 2); I.Add(k); I.Add(k + 2); I.Add(k + 3);
        }

        /// <summary>Horizontal grid of quads at height y (fine enough for lamp light to fall off smoothly).</summary>
        public void Grid(double x0, double z0, double x1, double z1, double y, double cell, double tex)
        {
            int nx = Math.Max(1, (int)Math.Ceiling((x1 - x0) / cell)), nz = Math.Max(1, (int)Math.Ceiling((z1 - z0) / cell));
            double sx = (x1 - x0) / nx, sz = (z1 - z0) / nz;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    double a = x0 + i * sx, b = z0 + j * sz;
                    int k = P.Count;
                    P.Add(new Point3D(a, y, b + sz)); P.Add(new Point3D(a + sx, y, b + sz)); P.Add(new Point3D(a + sx, y, b)); P.Add(new Point3D(a, y, b));
                    for (int q = 0; q < 4; q++) N.Add(new Vector3D(0, 1, 0));
                    T.Add(new Point(a / tex, (b + sz) / tex)); T.Add(new Point((a + sx) / tex, (b + sz) / tex)); T.Add(new Point((a + sx) / tex, b / tex)); T.Add(new Point(a / tex, b / tex));
                    I.Add(k); I.Add(k + 1); I.Add(k + 2); I.Add(k); I.Add(k + 2); I.Add(k + 3);
                }
        }

        /// <summary>Axis-aligned box; tex = metres per texture repeat (0 = stretch once).</summary>
        public void Box(double x0, double y0, double z0, double x1, double y1, double z1, double tex)
        {
            Func<double, double> r = delegate(double len) { return tex > 0 ? Math.Max(0.01, len / tex) : 1; };
            double dx = x1 - x0, dy = y1 - y0, dz = z1 - z0;
            Quad(new Point3D(x0, y0, z1), new Point3D(x1, y0, z1), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1), r(dx), r(dy)); // +z
            Quad(new Point3D(x1, y0, z0), new Point3D(x0, y0, z0), new Point3D(x0, y1, z0), new Point3D(x1, y1, z0), r(dx), r(dy)); // -z
            Quad(new Point3D(x1, y0, z1), new Point3D(x1, y0, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), r(dz), r(dy)); // +x
            Quad(new Point3D(x0, y0, z0), new Point3D(x0, y0, z1), new Point3D(x0, y1, z1), new Point3D(x0, y1, z0), r(dz), r(dy)); // -x
            Quad(new Point3D(x0, y1, z1), new Point3D(x1, y1, z1), new Point3D(x1, y1, z0), new Point3D(x0, y1, z0), r(dx), r(dz)); // top
            Quad(new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), new Point3D(x1, y0, z1), new Point3D(x0, y0, z1), r(dx), r(dz)); // bottom
        }

        /// <summary>Thin beam between two points (square section).</summary>
        public void Beam(Point3D a, Point3D b, double w)
        {
            Vector3D d = b - a; double len = d.Length; if (len < 1e-6) return;
            d.Normalize();
            Vector3D up = Math.Abs(d.Y) > 0.9 ? new Vector3D(1, 0, 0) : new Vector3D(0, 1, 0);
            Vector3D s = Vector3D.CrossProduct(d, up); s.Normalize(); s *= w / 2;
            Vector3D t = Vector3D.CrossProduct(s, d); t.Normalize(); t *= w / 2;
            Point3D[] c = { a + s + t, a - s + t, a - s - t, a + s - t };
            Point3D[] e = { b + s + t, b - s + t, b - s - t, b + s - t };
            for (int i = 0; i < 4; i++) { int j = (i + 1) % 4; Quad(c[i], c[j], e[j], e[i], 1, len / 2); }
        }

        public GeometryModel3D Model(Material m, bool twoSided)
        {
            MeshGeometry3D g = new MeshGeometry3D();
            g.Positions = P; g.Normals = N; g.TextureCoordinates = T; g.TriangleIndices = I;
            g.Freeze();
            GeometryModel3D gm = new GeometryModel3D(g, m);
            if (twoSided) gm.BackMaterial = m;
            gm.Freeze();
            return gm;
        }
    }

    static class Tex
    {
        static readonly Random rnd = new Random(11);

        public static Material Solid(Color c) { DiffuseMaterial m = new DiffuseMaterial(new SolidColorBrush(c)); m.Freeze(); return m; }

        public static Material Glow(Color c)
        {
            MaterialGroup g = new MaterialGroup();
            g.Children.Add(new DiffuseMaterial(new SolidColorBrush(c)));
            g.Children.Add(new EmissiveMaterial(new SolidColorBrush(c)));
            g.Freeze(); return g;
        }

        static Material Tiled(BitmapSource b, double opacity)
        {
            ImageBrush br = new ImageBrush(b);
            br.ViewportUnits = BrushMappingMode.Absolute; br.Viewport = new Rect(0, 0, 1, 1); br.TileMode = TileMode.Tile;
            br.Opacity = opacity;
            br.Freeze();
            DiffuseMaterial m = new DiffuseMaterial(br); m.Freeze();
            return m;
        }

        static BitmapSource Noise(int size, Color baseC, int amount, Action<byte[], int> extra)
        {
            byte[] px = new byte[size * size * 4];
            for (int i = 0; i < size * size; i++)
            {
                int n = rnd.Next(-amount, amount + 1);
                px[i * 4] = Clamp(baseC.B + n); px[i * 4 + 1] = Clamp(baseC.G + n); px[i * 4 + 2] = Clamp(baseC.R + n); px[i * 4 + 3] = 255;
            }
            if (extra != null) extra(px, size);
            BitmapSource b = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, px, size * 4);
            b.Freeze();
            return b;
        }

        static byte Clamp(int v) { return (byte)Math.Max(0, Math.Min(255, v)); }

        static void Darken(byte[] px, int i, int by) { px[i] = Clamp(px[i] - by); px[i + 1] = Clamp(px[i + 1] - by); px[i + 2] = Clamp(px[i + 2] - by); }

        /// <summary>Concrete slabs with joints and oil stains.</summary>
        public static Material Concrete()
        {
            return Tiled(Noise(256, Color.FromRgb(132, 134, 136), 12, delegate(byte[] px, int s)
            {
                for (int k = 0; k < 6; k++)
                {
                    int cx = rnd.Next(s), cy = rnd.Next(s), r = rnd.Next(10, 40);
                    for (int y = -r; y <= r; y++) for (int x = -r; x <= r; x++)
                        if (x * x + y * y < r * r) Darken(px, (((cy + y + s) % s) * s + ((cx + x + s) % s)) * 4, 10 - 10 * (x * x + y * y) / (r * r));
                }
                for (int i = 0; i < s; i++) { Darken(px, (i * s) * 4, 45); Darken(px, i * 4, 45); }
            }), 1);
        }

        /// <summary>Brick wall.</summary>
        public static Material Brick()
        {
            return Tiled(Noise(256, Color.FromRgb(150, 74, 54), 18, delegate(byte[] px, int s)
            {
                int bh = 32, bw = 64;
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        int row = y / bh; int off = (row % 2) * bw / 2;
                        bool mortar = y % bh < 3 || (x + off) % bw < 3;
                        if (mortar) { int i = (y * s + x) * 4; px[i] = 150; px[i + 1] = 160; px[i + 2] = 168; }
                    }
            }), 1);
        }

        /// <summary>Corrugated roof / wall sheeting.</summary>
        public static Material Sheet(Color c)
        {
            return Tiled(Noise(128, c, 6, delegate(byte[] px, int s)
            {
                for (int y = 0; y < s; y++) for (int x = 0; x < s; x++) if ((x / 8) % 2 == 0) Darken(px, (y * s + x) * 4, 18);
            }), 1);
        }

        /// <summary>Ballast stones.</summary>
        public static Material Ballast()
        {
            return Tiled(Noise(256, Color.FromRgb(110, 104, 96), 40, delegate(byte[] px, int s)
            {
                for (int k = 0; k < 900; k++)
                {
                    int cx = rnd.Next(s), cy = rnd.Next(s), r = rnd.Next(2, 6), shade = rnd.Next(-40, 40);
                    for (int y = -r; y <= r; y++) for (int x = -r; x <= r; x++)
                        if (x * x + y * y <= r * r) { int i = (((cy + y + s) % s) * s + ((cx + x + s) % s)) * 4; px[i] = Clamp(110 + shade); px[i + 1] = Clamp(106 + shade); px[i + 2] = Clamp(100 + shade); }
                }
            }), 1);
        }

        /// <summary>Glass skylight panel.</summary>
        public static Material Glass()
        {
            MaterialGroup g = new MaterialGroup();
            g.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(46, 190, 215, 235))));
            g.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 40));
            g.Freeze(); return g;
        }
    }

    public static class Scenery
    {
        /// <summary>Distance between rail centres, estimated from the loco's wheel parts (falls back to standard gauge).</summary>
        public static double RailCentre(ShapeModel m)
        {
            double maxX = 0;
            if (m != null)
            {
                Matrix3D[] w = ShapeReader.World(m, null);
                foreach (ShapeMesh me in m.Meshes)
                {
                    string n = (m.Matrices.Count > me.Matrix ? m.Matrices[me.Matrix].Name : "").ToUpperInvariant();
                    if (!n.StartsWith("WHEELS")) continue;
                    foreach (Point3D p in me.Positions) { Point3D q = w[me.Matrix].Transform(p); if (q.Y < 1.2) maxX = Math.Max(maxX, Math.Abs(q.X)); }
                }
            }
            // outer wheel face is ~0.07 m outside the rail centre
            double c = maxX > 0.5 && maxX < 1.2 ? maxX - 0.075 : 0.7535;
            return c;
        }

        static void Track(MeshBuilder rail, MeshBuilder sleepers, double rc, double z0, double z1, bool sleepersOn)
        {
            foreach (double s in new[] { -rc, rc })
            {
                rail.Box(s - 0.036, -0.16, z0, s + 0.036, -0.03, z1, 0);   // web + foot
                rail.Box(s - 0.037, -0.03, z0, s + 0.037, 0.0, z1, 0);     // head (top = 0: wheels sit on it)
                rail.Box(s - 0.075, -0.17, z0, s + 0.075, -0.15, z1, 0);   // foot
            }
            if (sleepersOn) for (double z = z0 + 0.3; z < z1; z += 0.6) sleepers.Box(-1.35, -0.36, z - 0.13, 1.35, -0.17, z + 0.13, 0.5);
        }

        static readonly Dictionary<string, ShapeModel> trackCache = new Dictionary<string, ShapeModel>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The real DB Tracks straight (GLOBAL\SHAPES\A1t250mStrt.s) under the loco, widened to the
        /// loco's wheel gauge and lowered so its rail head meets the wheels. Null when it is not installed.</summary>
        public static Model3DGroup DbTrack(string orRoot, double railCentre)
        {
            if (string.IsNullOrEmpty(orRoot)) return null;
            string shapes = System.IO.Path.Combine(System.IO.Path.Combine(orRoot, "GLOBAL"), "SHAPES");
            string textures = System.IO.Path.Combine(System.IO.Path.Combine(orRoot, "GLOBAL"), "TEXTURES");
            string file = null;
            foreach (string n in new[] { "A1t250mStrt.s", "A1t100mStrt.s" }) { string f = System.IO.Path.Combine(shapes, n); if (System.IO.File.Exists(f)) { file = f; break; } }
            if (file == null) return null;
            ShapeModel sm;
            lock (trackCache) if (!trackCache.TryGetValue(file, out sm)) { sm = ShapeReader.Load(file); trackCache[file] = sm; }
            if (sm.Error != null || sm.Meshes.Count == 0) return null;
            Model3DGroup g = new Model3DGroup();
            Dictionary<string, Material> mats = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
            foreach (ShapeMesh me in sm.Meshes)
            {
                MeshGeometry3D mesh = new MeshGeometry3D();
                mesh.Positions = new Point3DCollection(me.Positions); mesh.Normals = new Vector3DCollection(me.Normals);
                mesh.TextureCoordinates = new PointCollection(me.UVs); mesh.TriangleIndices = new Int32Collection(me.Indices);
                mesh.Freeze();
                Material mat;
                string tex = me.Texture ?? "";
                if (!mats.TryGetValue(tex, out mat))
                {
                    int ok = 0, miss = 0;
                    mat = LocoViewer.MakeMaterial(textures, me.Texture, ref ok, ref miss);
                    mats[tex] = mat;
                }
                GeometryModel3D gm = new GeometryModel3D(mesh, mat); gm.BackMaterial = mat;
                g.Children.Add(gm);
            }
            // DB rails sit at x = +-0.7175 with the rail head 0.325 m above the shape origin; the 250 m piece starts at z = 0
            double len = -sm.Bounds.Z;   // shape runs along -z after the coordinate flip
            Transform3DGroup tr = new Transform3DGroup();
            tr.Children.Add(new ScaleTransform3D(railCentre / 0.7175, 1, 1));
            tr.Children.Add(new TranslateTransform3D(0, -0.325, len / 2));
            g.Transform = tr;
            return g;
        }

        public static Model3DGroup OpenTrack(ShapeModel m, Model3DGroup dbTrack)
        {
            double rc = RailCentre(m), L = 70;
            Model3DGroup g = new Model3DGroup();
            MeshBuilder ground = new MeshBuilder(), ballast = new MeshBuilder(), rail = new MeshBuilder(), sl = new MeshBuilder();
            ground.Quad(new Point3D(-40, -0.5, L), new Point3D(40, -0.5, L), new Point3D(40, -0.5, -L), new Point3D(-40, -0.5, -L), 20, 35);
            // ballast shoulder
            ballast.Quad(new Point3D(-1.7, -0.36, L), new Point3D(1.7, -0.36, L), new Point3D(1.7, -0.36, -L), new Point3D(-1.7, -0.36, -L), 1.7, 35);
            ballast.Quad(new Point3D(-2.6, -0.5, L), new Point3D(-1.7, -0.36, L), new Point3D(-1.7, -0.36, -L), new Point3D(-2.6, -0.5, -L), 0.5, 35);
            ballast.Quad(new Point3D(1.7, -0.36, L), new Point3D(2.6, -0.5, L), new Point3D(2.6, -0.5, -L), new Point3D(1.7, -0.36, -L), 0.5, 35);
            if (dbTrack == null) Track(rail, sl, rc, -L, L, true);
            g.Children.Add(ground.Model(Tex.Solid(Color.FromRgb(58, 72, 52)), false));
            if (dbTrack != null) g.Children.Add(dbTrack); else g.Children.Add(ballast.Model(Tex.Ballast(), true));
            g.Children.Add(sl.Model(Tex.Solid(Color.FromRgb(120, 118, 112)), false));
            g.Children.Add(rail.Model(Tex.Solid(Color.FromRgb(140, 142, 148)), false));
            return g;
        }

        /// <summary>A loco shed: concrete floor, inspection pit, steel frame, trusses, skylights, lamps, gantry crane, benches.</summary>
        public static Model3DGroup Shed(ShapeModel m, bool electric, double wireHeight, bool hasDbTrack, out List<Point3D> lamps, out Model3DGroup roofOut, out Model3DGroup nightPools)
        {
            double rc = RailCentre(m);
            double L = 36, W = 9, H = 8.5;                      // half length, half width, eaves height
            Model3DGroup g = new Model3DGroup();
            lamps = new List<Point3D>();
            MeshBuilder floor = new MeshBuilder(), pit = new MeshBuilder(), rail = new MeshBuilder(), sl = new MeshBuilder(), steel = new MeshBuilder(),
                yellow = new MeshBuilder(), brick = new MeshBuilder(), roof = new MeshBuilder(), glass = new MeshBuilder(), lamp = new MeshBuilder(),
                bench = new MeshBuilder(), wire = new MeshBuilder(), stripes = new MeshBuilder(), cabinet = new MeshBuilder();
            // the track itself is the real DB Tracks shape (added by the viewer); the floor stops at the
            // ballast edge and sits a little below the rail head, like an embedded shed track
            double tb = 1.85;                                    // half width of the track bed left open
            double fy = -0.17;                                   // floor level (rail head is y = 0)
            double pw = tb, pd = 0, pz = 0;                      // (kept for the light-pool code)
            floor.Grid(-W, -L, -tb, L, fy, 1.0, 3);
            floor.Grid(tb, -L, W, L, fy, 1.0, 3);
            // kerbs along the track bed
            foreach (int sd in new[] { -1, 1 }) floor.Box(sd > 0 ? tb : -tb - 0.15, -0.35, -L, sd > 0 ? tb + 0.15 : -tb, fy + 0.02, L, 1);
            // outside apron
            floor.Quad(new Point3D(-W - 8, fy - 0.02, L + 18), new Point3D(-tb, fy - 0.02, L + 18), new Point3D(-tb, fy - 0.02, -L - 18), new Point3D(-W - 8, fy - 0.02, -L - 18), 6, 15);
            floor.Quad(new Point3D(tb, fy - 0.02, L + 18), new Point3D(W + 8, fy - 0.02, L + 18), new Point3D(W + 8, fy - 0.02, -L - 18), new Point3D(tb, fy - 0.02, -L - 18), 6, 15);
            if (!hasDbTrack) Track(rail, sl, rc, -L - 18, L + 18, true);   // fallback when DB Tracks is not installed
            // yellow safety lines along the pit
            foreach (double x in new[] { -tb - 0.6, tb + 0.6 }) stripes.Quad(new Point3D(x - 0.06, fy + 0.005, L), new Point3D(x + 0.06, fy + 0.005, L), new Point3D(x + 0.06, fy + 0.005, -L), new Point3D(x - 0.06, fy + 0.005, -L), 1, 1);
            // columns + trusses every 6 m
            for (double z = -L; z <= L + 0.01; z += 6)
            {
                foreach (double x in new[] { -W, W })
                {
                    steel.Box(x - 0.18, 0, z - 0.18, x + 0.18, H, z + 0.18, 0);                      // column
                    steel.Box(x - 0.3, 0, z - 0.3, x + 0.3, 0.25, z + 0.3, 0);                       // base plate
                }
                Point3D lt = new Point3D(-W, H, z), rt = new Point3D(W, H, z), apex = new Point3D(0, H + 3.2, z);
                steel.Beam(lt, rt, 0.16);                                                             // tie beam
                steel.Beam(lt, apex, 0.2); steel.Beam(rt, apex, 0.2);                                // rafters
                for (int k = 1; k < 6; k++)                                                          // web members
                {
                    double t = k / 6.0;
                    Point3D onTie = new Point3D(-W + 2 * W * t, H, z);
                    Point3D onRafter = t < 0.5 ? lt + (apex - lt) * (t * 2) : rt + (apex - rt) * ((1 - t) * 2);
                    steel.Beam(onTie, onRafter, 0.08);
                }
                // lamps under the tie beam
                foreach (double x in new[] { -3.5, 3.5 })
                {
                    steel.Box(x - 0.02, H - 1.2, z - 0.02, x + 0.02, H, z + 0.02, 0);
                    lamp.Box(x - 0.35, H - 1.35, z - 0.12, x + 0.35, H - 1.2, z + 0.12, 0);
                    lamps.Add(new Point3D(x, H - 1.4, z));
                }
            }
            // purlins and roof: sheet roof with a glazed strip each side of the ridge
            for (int k = 0; k <= 6; k++)
            {
                double t = k / 6.0;
                foreach (int side in new[] { -1, 1 })
                {
                    Point3D a = new Point3D(side * W * (1 - t), H + 3.2 * t, -L), b = new Point3D(side * W * (1 - t), H + 3.2 * t, L);
                    steel.Beam(a + new Vector3D(0, 0.1, 0), b + new Vector3D(0, 0.1, 0), 0.1);
                }
            }
            foreach (int side in new[] { -1, 1 })
            {
                Point3D eL = new Point3D(side * W, H + 0.2, -L), eR = new Point3D(side * W, H + 0.2, L);
                Point3D mL = new Point3D(side * W * 0.45, H + 0.2 + 3.2 * 0.55, -L), mR = new Point3D(side * W * 0.45, H + 0.2 + 3.2 * 0.55, L);
                Point3D rL = new Point3D(0, H + 3.4, -L), rR = new Point3D(0, H + 3.4, L);
                roof.Quad(eL, eR, mR, mL, 30, 3);
                glass.Quad(mL, mR, rR, rL, 30, 1);
            }
            // low brick walls along the sides with windows above left open, corrugated cladding high up
            foreach (int side in new[] { -1, 1 })
            {
                double x = side * W;
                brick.Box(Math.Min(x, x + side * 0.25), 0, -L, Math.Max(x, x + side * 0.25), 1.2, L, 1.5);
            }
            // gantry crane across the shed near one end
            double cz = -L + 9;
            foreach (double x in new[] { -W + 0.4, W - 0.4 }) yellow.Box(x - 0.15, H - 1.9, -L, x + 0.15, H - 1.6, cz + 5, 0);    // runway beams (crane bay only)
            yellow.Box(-W + 0.3, H - 1.6, cz - 0.35, W - 0.3, H - 1.1, cz + 0.35, 0);                                      // bridge girder
            yellow.Box(-0.6, H - 2.0, cz - 0.5, 0.6, H - 1.6, cz + 0.5, 0);                                                // trolley
            steel.Box(-0.03, H - 4.5, cz - 0.03, 0.03, H - 2.0, cz + 0.03, 0);                                              // rope
            yellow.Box(-0.25, H - 4.9, cz - 0.15, 0.25, H - 4.5, cz + 0.15, 0);                                            // hook block
            // workbenches and tool cabinets along the walls
            for (double z = -L + 4; z < L - 4; z += 12)
            {
                foreach (int side in new[] { -1, 1 })
                {
                    double x = side * (W - 0.9);
                    bench.Box(x - 0.45, 0.85, z - 1.2, x + 0.45, 0.92, z + 1.2, 0);           // top
                    foreach (double dz in new[] { -1.1, 1.1 }) foreach (double dx in new[] { -0.4, 0.4 }) steel.Box(x + dx - 0.03, 0, z + dz - 0.03, x + dx + 0.03, 0.85, z + dz + 0.03, 0);
                    cabinet.Box(x - 0.3, 0, z + 2.0, x + 0.3, 1.9, z + 3.0, 0);               // tool cabinet
                }
            }
            // overhead contact wire for electric locos (inside the shed it is often isolated, but useful to see pantograph reach)
            if (electric)
            {
                double ch = wireHeight;
                wire.Box(-0.006, ch, -L - 18, 0.006, ch + 0.012, L + 18, 0);
                wire.Box(-0.006, ch + 1.1, -L - 18, 0.006, ch + 1.112, L + 18, 0);
                for (double z = -L - 18; z <= L + 18; z += 9) wire.Box(-0.004, ch, z - 0.004, 0.004, ch + 1.1, z + 0.004, 0);
                for (double z = -L; z <= L + 0.01; z += 12) steel.Box(-W, ch + 1.2, z - 0.05, W, ch + 1.3, z + 0.05, 0);
            }
            g.Children.Add(floor.Model(Tex.Concrete(), false));

            g.Children.Add(sl.Model(Tex.Solid(Color.FromRgb(118, 116, 110)), false));
            g.Children.Add(rail.Model(Tex.Solid(Color.FromRgb(150, 152, 158)), false));
            g.Children.Add(stripes.Model(Tex.Glow(Color.FromRgb(230, 190, 40)), false));
            g.Children.Add(brick.Model(Tex.Brick(), false));
            g.Children.Add(steel.Model(Tex.Solid(Color.FromRgb(70, 84, 104)), true));
            g.Children.Add(yellow.Model(Tex.Solid(Color.FromRgb(226, 180, 30)), false));
            g.Children.Add(bench.Model(Tex.Solid(Color.FromRgb(120, 90, 60)), false));
            g.Children.Add(cabinet.Model(Tex.Solid(Color.FromRgb(46, 110, 160)), false));
            g.Children.Add(lamp.Model(Tex.Glow(Color.FromRgb(255, 244, 214)), false));
            if (electric) g.Children.Add(wire.Model(Tex.Solid(Color.FromRgb(160, 120, 70)), false));
            // warm pools of light on the floor under each lamp, shown at night
            nightPools = new Model3DGroup();
            MeshBuilder pools = new MeshBuilder();
            foreach (Point3D lp in lamps)
            {
                double r = 4.2, y = -0.155;
                pools.Quad(new Point3D(lp.X - r, y, lp.Z + r), new Point3D(lp.X + r, y, lp.Z + r), new Point3D(lp.X + r, y, lp.Z - r), new Point3D(lp.X - r, y, lp.Z - r), 1, 1);
            }
            RadialGradientBrush pg = new RadialGradientBrush();
            pg.GradientStops.Add(new GradientStop(Color.FromArgb(150, 255, 196, 120), 0));
            pg.GradientStops.Add(new GradientStop(Color.FromArgb(60, 255, 180, 100), 0.45));
            pg.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 170, 90), 1));
            pg.Freeze();
            MaterialGroup pm = new MaterialGroup();
            pm.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))));
            pm.Children.Add(new EmissiveMaterial(pg));
            pm.Freeze();
            nightPools.Children.Add(pools.Model(pm, false));
            roofOut = new Model3DGroup();
            roofOut.Children.Add(roof.Model(Tex.Sheet(Color.FromRgb(96, 104, 116)), true));
            roofOut.Children.Add(glass.Model(Tex.Glass(), true));     // translucent, added after everything else
            return g;
        }
    }
}
