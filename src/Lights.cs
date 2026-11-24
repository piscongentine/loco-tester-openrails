// Loco Tester - realistic loco lights: camera-facing glare sprites (brighter when you look into them),
// soft fading headlight beams, blinking states, and the .eng light conditions (headlight, unit,
// penalty / emergency, time of day, weather, control, service).
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace LocoTester
{
    class LightRenderer
    {
        public readonly Model3DGroup Group = new Model3DGroup();
        readonly Model3DGroup flares = new Model3DGroup();
        readonly Dictionary<uint, Material> mats = new Dictionary<uint, Material>();
        readonly Model3DGroup beams = new Model3DGroup();
        readonly List<PointLight> lamps = new List<PointLight>();
        string beamKey = "";
        internal static readonly BitmapSource flareTex = MakeFlare();

        public int Shown;            // lights on right now (for the info line)

        public LightRenderer()
        {
            Group.Children.Add(beams);
            Group.Children.Add(flares);
        }

        /// <summary>White-hot core, coloured halo and a faint horizontal streak.</summary>
        static BitmapSource MakeFlare()
        {
            int s = 128;
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                RadialGradientBrush halo = new RadialGradientBrush();
                halo.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0));
                halo.GradientStops.Add(new GradientStop(Color.FromArgb(230, 255, 255, 255), 0.08));
                halo.GradientStops.Add(new GradientStop(Color.FromArgb(120, 255, 255, 255), 0.22));
                halo.GradientStops.Add(new GradientStop(Color.FromArgb(40, 255, 255, 255), 0.5));
                halo.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
                dc.DrawEllipse(halo, null, new Point(s / 2.0, s / 2.0), s / 2.0, s / 2.0);
                LinearGradientBrush streak = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
                streak.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
                streak.GradientStops.Add(new GradientStop(Color.FromArgb(90, 255, 255, 255), 0.5));
                streak.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
                dc.DrawRectangle(streak, null, new Rect(0, s / 2.0 - 2, s, 4));
            }
            RenderTargetBitmap b = new RenderTargetBitmap(s, s, 96, 96, PixelFormats.Pbgra32);
            b.Render(dv); b.Freeze();
            return b;
        }

        static bool IsRed(uint c) { int r = (int)((c >> 16) & 255), g = (int)((c >> 8) & 255), b = (int)(c & 255); return r > 180 && g < 90 && b < 90; }

        public static bool Visible(LightDef d, AnimState s, bool night)
        {
            // Realistic tail lights: red lamps at the trailing end burn whenever the headlights are
            // on (dim or bright); red lamps at the leading end stay off. Many .eng files only tie the tail
            // light to one headlight setting, which is not how a real loco runs.
            if (d.States.Count > 0 && IsRed(d.States[0].Colour) && d.Type == 0 && d.Penalty == 0)
            {
                bool atRear = d.States[0].Z < 0;
                bool trailing = s.Reverser == -1 ? !atRear : atRear;
                if (!trailing) return false;
                return s.Headlight >= 1 && !s.RearUnit;
            }
            bool hl = d.Headlight == 0 || (d.Headlight == 1 && s.Headlight == 0) || (d.Headlight == 2 && s.Headlight == 1) || (d.Headlight == 3 && s.Headlight == 2);
            // Unit 2/4 = facing the direction of travel, 3/5 = turned round. Driving in reverse makes the
            // other end the front, so only the end the loco is heading toward shows its head / dim lights.
            // A loco on its own is both the first and the last vehicle: head / dim lights at the end it is
            // heading toward (Unit 2, or 3 when reversed) and red tail lights at the other end (Unit 4 / 5).
            // With a train coupled behind (RearUnit = "train behind") the tail lights are left off.
            bool rev = s.Reverser == -1;
            bool lead = d.Unit == (rev ? 3 : 2), tail = d.Unit == (rev ? 5 : 4);
            bool unit = d.Unit == 0 || lead || (tail && !s.RearUnit);
            bool pen = d.Penalty == 0 || (d.Penalty == 1 && !s.Penalty) || (d.Penalty == 2 && s.Penalty);
            bool tod = d.TimeOfDay == 0 || (d.TimeOfDay == 1 && !night) || (d.TimeOfDay == 2 && night);
            bool wx = d.Weather == 0 || (d.Weather == 1 && !s.Rain) || (d.Weather == 2 && s.Rain);
            bool ctl = d.Control == 0 || d.Control == 2;      // the player drives it
            bool svc = d.Service == 0 || d.Service == 2;      // in service
            return hl && unit && pen && tod && wx && ctl && svc;
        }

        public void Update(Loco loco, AnimState s, bool night, Point3D camPos, Vector3D look, Vector3D up)
        {
            Shown = 0;
            if (loco == null) { flares.Children.Clear(); beams.Children.Clear(); return; }
            Vector3D right = Vector3D.CrossProduct(look, up); right.Normalize();
            Vector3D upv = Vector3D.CrossProduct(right, look); upv.Normalize();
            Dictionary<uint, MeshGeometry3D> byColour = new Dictionary<uint, MeshGeometry3D>();
            System.Text.StringBuilder key = new System.Text.StringBuilder(night ? "n" : "d");
            List<KeyValuePair<LightState, LightDef>> beamList = new List<KeyValuePair<LightState, LightDef>>();
            foreach (LightDef d in loco.Lights)
            {
                if (d.States.Count == 0 || !Visible(d, s, night)) continue;
                LightState st = d.States[0];
                double fade = 1;
                if (d.States.Count > 1)
                {
                    double total = 0; foreach (LightState x in d.States) total += Math.Max(0.05, x.Duration);
                    double t = s.Time % total;
                    foreach (LightState x in d.States) { double du = Math.Max(0.05, x.Duration); if (t <= du) { st = x; fade = Math.Min(1, Math.Min(t, du - t) / 0.06 + 0.25); break; } t -= du; }
                }
                uint c = st.Colour;
                byte a = (byte)(c >> 24);
                if (a == 0) continue;
                Shown++;
                Color col = Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c);
                Point3D p = new Point3D(st.X, st.Y, -st.Z);
                bool front = st.Z >= 0;
                // glare is strongest when looking into the lamp
                Vector3D facing = front ? new Vector3D(0, 0, -1) : new Vector3D(0, 0, 1);
                Vector3D toCam = camPos - p; double dist = toCam.Length; toCam.Normalize();
                double into = Math.Max(0, Vector3D.DotProduct(facing, toCam));
                double baseSize = d.Type == 1 ? (s.Headlight == 2 ? 0.55 : 0.32) : Math.Max(0.12, Math.Min(0.4, st.Radius * 0.6));
                double size = baseSize * (0.35 + 0.65 * into) * (night ? 1.6 : 1.0) * fade;
                size *= 1 + Math.Min(1.5, dist / 60);   // keep visible from far away
                double alpha = (a / 255.0) * (0.45 + 0.55 * into);
                // white-hot core blended toward the lamp colour; alpha in 4 steps so materials can be shared
                uint ak = (uint)Math.Max(1, Math.Min(4, Math.Round(alpha * 4)));
                uint ck = (ak << 24) | (c & 0xFFFFFF);
                MeshGeometry3D mesh;
                if (!byColour.TryGetValue(ck, out mesh)) { mesh = new MeshGeometry3D(); byColour[ck] = mesh; }
                AddSprite(mesh.Positions, mesh.TextureCoordinates, mesh.TriangleIndices, p + toCam * 0.05, right, upv, size, col, alpha);
                if (d.Type == 1) { beamList.Add(new KeyValuePair<LightState, LightDef>(st, d)); key.Append(st.GetHashCode()).Append(','); }
            }
            flares.Children.Clear();
            foreach (KeyValuePair<uint, MeshGeometry3D> kv in byColour)
            {
                Material mat = FlareMaterial(kv.Key);
                GeometryModel3D gm = new GeometryModel3D(kv.Value, mat); gm.BackMaterial = mat;
                flares.Children.Add(gm);
            }

            key.Append(s.Headlight);
            if (key.ToString() != beamKey)
            {
                beamKey = key.ToString();
                beams.Children.Clear();
                foreach (KeyValuePair<LightState, LightDef> kv in beamList)
                {
                    LightState st = kv.Key;
                    uint c = st.Colour;
                    Color col = Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c);
                    Point3D p = new Point3D(st.X, st.Y, -st.Z);
                    bool back = st.Z < 0;
                    double len = (s.Headlight == 2 ? 14 : 7) * (night ? 1.3 : 0.8);
                    beams.Children.Add(Beam(p, len, back, col, night ? 0.3 : 0.12));
                    if (night)
                    {
                        PointLight pl = new PointLight(col, new Point3D(p.X, Math.Max(0.6, p.Y - 1.2), p.Z + (back ? 5 : -5)));
                        pl.Range = 30; pl.ConstantAttenuation = 1; pl.LinearAttenuation = 0.12;
                        beams.Children.Add(pl);
                    }
                }
            }
        }

        Material FlareMaterial(uint key)
        {
            Material m;
            if (mats.TryGetValue(key, out m)) return m;
            byte a = (byte)(((key >> 24) & 0xFF) * 63);
            Color lamp = Color.FromRgb((byte)(key >> 16), (byte)(key >> 8), (byte)key);
            // tint: mostly the lamp colour, lifted toward white like a real glowing lens
            Color tint = Color.FromArgb(a, (byte)(lamp.R * 0.75 + 64), (byte)(lamp.G * 0.75 + 64), (byte)(lamp.B * 0.75 + 64));
            ImageBrush br = new ImageBrush(flareTex) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) };
            br.Freeze();
            MaterialGroup mg = new MaterialGroup();
            mg.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))));
            EmissiveMaterial em = new EmissiveMaterial(br); em.Color = tint;
            mg.Children.Add(em);
            mg.Freeze();
            mats[key] = mg;
            return mg;
        }

        static void AddSprite(Point3DCollection pos, PointCollection uv, Int32Collection idx, Point3D p, Vector3D r, Vector3D u, double s, Color c, double alpha)
        {
            int b = pos.Count;
            Vector3D a = r * s, v = u * s;
            pos.Add(p - a - v); pos.Add(p + a - v); pos.Add(p + a + v); pos.Add(p - a + v);
            uv.Add(new Point(0, 1)); uv.Add(new Point(1, 1)); uv.Add(new Point(1, 0)); uv.Add(new Point(0, 0));
            idx.Add(b); idx.Add(b + 1); idx.Add(b + 2); idx.Add(b); idx.Add(b + 2); idx.Add(b + 3);
        }

        /// <summary>Soft cone whose light fades out along its length.</summary>
        static Model3D Beam(Point3D p, double len, bool back, Color c, double opacity)
        {
            int seg = 20;
            MeshGeometry3D m = new MeshGeometry3D();
            double dir = back ? 1 : -1, r = len * 0.2;
            for (int i = 0; i <= seg; i++)
            {
                double th = 2 * Math.PI * i / seg;
                m.Positions.Add(new Point3D(p.X, p.Y, p.Z)); m.TextureCoordinates.Add(new Point(0, 0.5));
                m.Positions.Add(new Point3D(p.X + Math.Cos(th) * r, p.Y + Math.Sin(th) * r * 0.6 - len * 0.05, p.Z + dir * len)); m.TextureCoordinates.Add(new Point(1, 0.5));
            }
            for (int i = 0; i < seg; i++) { int a = i * 2; m.TriangleIndices.Add(a); m.TriangleIndices.Add(a + 1); m.TriangleIndices.Add(a + 3); }
            LinearGradientBrush g = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(opacity * 90), c.R, c.G, c.B), 0.35));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
            MaterialGroup mg = new MaterialGroup();
            mg.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))));
            mg.Children.Add(new EmissiveMaterial(g));
            GeometryModel3D gm = new GeometryModel3D(m, mg); gm.BackMaterial = mg;
            return gm;
        }
    }
}
