// Loco Tester - smoke / steam particles drawn as soft, camera-facing textured puffs.
// One mesh for all particles (rebuilt every frame), a generated texture atlas for
// age (fading) and colour (diesel exhaust grey / steam white), back-to-front sorted.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace LocoTester
{
    class SmokeSystem
    {
        class P { public Point3D Pos; public Vector3D Vel; public double Age, Life, Size, Spin, Angle; public int Em, Shade; public bool Alive, Steam; }
        const int Max = 420, Cols = 8;
        readonly P[] parts = new P[Max];
        readonly double[] acc = new double[32];
        readonly Random rnd = new Random();
        readonly GeometryModel3D model;
        public readonly Model3DGroup Group = new Model3DGroup();
        double time;

        public SmokeSystem()
        {
            for (int i = 0; i < Max; i++) parts[i] = new P();
            ImageBrush br = new ImageBrush(Atlas());
            br.ViewportUnits = BrushMappingMode.Absolute; br.Viewport = new Rect(0, 0, 1, 1);
            br.Freeze();
            MaterialGroup mg = new MaterialGroup();
            mg.Children.Add(new DiffuseMaterial(br));
            mg.Children.Add(new EmissiveMaterial(new ImageBrush(Atlas()) { Opacity = 0.35, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) }));
            mg.Freeze();
            model = new GeometryModel3D(new MeshGeometry3D(), mg);
            model.BackMaterial = mg;
            Group.Children.Add(model);
        }

        /// <summary>8 columns (young to old, fading) x 3 rows (dark diesel, grey, white steam) of soft billowy puffs.</summary>
        static BitmapSource Atlas()
        {
            int cell = 96, w = cell * Cols, h = cell * 3;
            DrawingVisual dv = new DrawingVisual();
            Random r = new Random(5);
            Color[] rows = { Color.FromRgb(46, 46, 50), Color.FromRgb(120, 122, 128), Color.FromRgb(238, 240, 244) };
            using (DrawingContext dc = dv.RenderOpen())
            {
                for (int row = 0; row < 3; row++)
                    for (int c = 0; c < Cols; c++)
                    {
                        double alpha = 0.85 * Math.Pow(1 - c / (double)Cols, 1.3);
                        Point centre = new Point(c * cell + cell / 2.0, row * cell + cell / 2.0);
                        // several overlapping soft blobs make a lumpy, natural puff
                        for (int k = 0; k < 7; k++)
                        {
                            double ang = r.NextDouble() * Math.PI * 2, d = r.NextDouble() * cell * 0.16;
                            double rad = cell * (0.2 + r.NextDouble() * 0.14);
                            Color col = rows[row];
                            int shade = r.Next(-14, 14);
                            col = Color.FromRgb((byte)Math.Max(0, Math.Min(255, col.R + shade)), (byte)Math.Max(0, Math.Min(255, col.G + shade)), (byte)Math.Max(0, Math.Min(255, col.B + shade)));
                            RadialGradientBrush g = new RadialGradientBrush();
                            g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha * 150), col.R, col.G, col.B), 0));
                            g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha * 70), col.R, col.G, col.B), 0.55));
                            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, col.R, col.G, col.B), 1));
                            dc.DrawEllipse(g, null, new Point(centre.X + Math.Cos(ang) * d, centre.Y + Math.Sin(ang) * d), rad, rad);
                        }
                    }
            }
            RenderTargetBitmap bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        public void Reset() { foreach (P p in parts) p.Alive = false; for (int i = 0; i < acc.Length; i++) acc[i] = 0; model.Geometry = new MeshGeometry3D(); }

        public void Update(Loco l, AnimState s, double dt, Point3D camPos, Vector3D look, Vector3D up)
        {
            if (l == null) return;
            time += dt;
            bool diesel = (l.EngineType ?? "").IndexOf("Diesel", StringComparison.OrdinalIgnoreCase) >= 0;
            for (int e = 0; e < l.Emitters.Count && e < acc.Length; e++)
            {
                Emitter em = l.Emitters[e];
                string n = em.Name.ToUpperInvariant();
                double rate = 0;
                if (s.Smoke && s.Power)
                {
                    if (n.StartsWith("EXHAUST") && diesel) rate = 5 + 35 * s.Throttle;
                    else if (n.StartsWith("STACK")) rate = 6 + 34 * s.Throttle;
                    else if (n.StartsWith("WHISTLE")) rate = s.Horn ? 40 : 0;
                    else if (n.StartsWith("CYLINDER") || n.StartsWith("DRAIN")) rate = (s.Throttle > 0.05 && s.Speed < 6) ? 25 : 0;
                }
                acc[e] += rate * dt;
                while (acc[e] >= 1) { acc[e] -= 1; Spawn(em, e, s, diesel); }
            }

            // simulate
            List<P> live = new List<P>(Max);
            for (int i = 0; i < Max; i++)
            {
                P p = parts[i];
                if (!p.Alive) continue;
                p.Age += dt;
                if (p.Age >= p.Life) { p.Alive = false; continue; }
                // drag, buoyancy, a gentle breeze and swirl, and the air the moving loco leaves behind
                Vector3D swirl = new Vector3D(Math.Sin(time * 1.3 + p.Angle * 3) * 0.25, 0, Math.Cos(time * 1.1 + p.Angle * 2) * 0.25);
                p.Vel = p.Vel * (1 - 1.1 * dt) + new Vector3D(0.25, (p.Steam ? 0.9 : 1.2), s.Speed * 1.1) * dt + swirl * dt;
                p.Pos += p.Vel * dt;
                p.Angle += p.Spin * dt;
                live.Add(p);
            }
            // back to front
            live.Sort(delegate(P a, P b) { return ((b.Pos - camPos).LengthSquared).CompareTo((a.Pos - camPos).LengthSquared); });

            Vector3D right = Vector3D.CrossProduct(look, up); if (right.Length < 1e-6) right = new Vector3D(1, 0, 0); right.Normalize();
            Vector3D upv = Vector3D.CrossProduct(right, look); upv.Normalize();
            Point3DCollection pos = new Point3DCollection(live.Count * 4);
            PointCollection uv = new PointCollection(live.Count * 4);
            Int32Collection idx = new Int32Collection(live.Count * 6);
            Vector3DCollection nrm = new Vector3DCollection(live.Count * 4);
            Vector3D toCam = -look; toCam.Normalize();
            double cw = 1.0 / Cols, ch = 1.0 / 3;
            foreach (P p in live)
            {
                double k = p.Age / p.Life;
                double size = p.Size * (0.5 + 3.2 * Math.Sqrt(k));          // grows fast, then slower
                double ca = Math.Cos(p.Angle) * size, sa = Math.Sin(p.Angle) * size;
                Vector3D ax = right * ca + upv * sa, ay = -right * sa + upv * ca;
                int b = pos.Count;
                pos.Add(p.Pos - ax - ay); pos.Add(p.Pos + ax - ay); pos.Add(p.Pos + ax + ay); pos.Add(p.Pos - ax + ay);
                for (int i = 0; i < 4; i++) nrm.Add(toCam);
                int col = Math.Min(Cols - 1, (int)(k * Cols));
                double u0 = col * cw + 0.002, v0 = p.Shade * ch + 0.002, u1 = u0 + cw - 0.004, v1 = v0 + ch - 0.004;
                uv.Add(new Point(u0, v1)); uv.Add(new Point(u1, v1)); uv.Add(new Point(u1, v0)); uv.Add(new Point(u0, v0));
                idx.Add(b); idx.Add(b + 1); idx.Add(b + 2); idx.Add(b); idx.Add(b + 2); idx.Add(b + 3);
            }
            MeshGeometry3D m = new MeshGeometry3D();
            m.Positions = pos; m.TextureCoordinates = uv; m.TriangleIndices = idx; m.Normals = nrm;
            model.Geometry = m;
        }

        void Spawn(Emitter em, int e, AnimState s, bool diesel)
        {
            for (int i = 0; i < Max; i++)
            {
                P p = parts[i];
                if (p.Alive) continue;
                string n = em.Name.ToUpperInvariant();
                p.Alive = true; p.Age = 0; p.Em = e;
                p.Steam = em.Steam && !n.StartsWith("STACK");
                double j = em.Width * 0.35;
                p.Pos = new Point3D(em.X + (rnd.NextDouble() - 0.5) * j, em.Y + 0.05, -em.Z + (rnd.NextDouble() - 0.5) * j);
                Vector3D dir = new Vector3D(em.DX, em.DY, -em.DZ);
                if (dir.Length < 1e-6) dir = new Vector3D(0, 1, 0);
                dir.Normalize();
                double sp = (diesel ? 3 + 6 * s.Throttle : 4) * (0.75 + 0.5 * rnd.NextDouble());
                p.Vel = dir * sp + new Vector3D((rnd.NextDouble() - 0.5) * 0.8, 0, (rnd.NextDouble() - 0.5) * 0.8);
                p.Size = Math.Max(0.14, em.Width * 0.55) * (diesel ? 0.9 + 0.7 * s.Throttle : 1);
                p.Life = (diesel ? 2.6 + 1.2 * s.Throttle : 3.2) * (0.75 + 0.5 * rnd.NextDouble());
                p.Angle = rnd.NextDouble() * Math.PI * 2;
                p.Spin = (rnd.NextDouble() - 0.5) * 1.2;
                // shade: diesel gets darker with throttle; steam chimney grey/white; other steam white
                if (!em.Steam) p.Shade = s.Throttle > 0.45 ? 0 : (rnd.NextDouble() < 0.5 ? 0 : 1);
                else if (n.StartsWith("STACK")) p.Shade = s.Throttle > 0.5 ? 1 : 2;
                else p.Shade = 2;
                return;
            }
        }
    }
}
