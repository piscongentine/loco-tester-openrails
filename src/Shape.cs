// Loco Tester - reader for binary MSTS shape files (.s) and textures (.ace).
// Token ids worked out from the files themselves (block id / size / label layout).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace LocoTester
{
    public class ShapeMatrix
    {
        public string Name;
        public Matrix3D Local;
        public int Parent = -1;
        public List<AnimKey> Rot = new List<AnimKey>();
        public List<AnimKey> Pos = new List<AnimKey>();
        public bool Animated { get { return Rot.Count > 1 || Pos.Count > 1; } }
        public double MaxFrame
        {
            get
            {
                double m = 0;
                foreach (AnimKey k in Rot) m = Math.Max(m, k.Frame);
                foreach (AnimKey k in Pos) m = Math.Max(m, k.Frame);
                return m;
            }
        }
    }

    public struct AnimKey
    {
        public double Frame;
        public Quaternion Q;
        public Vector3D P;
    }

    public class ShapeMesh
    {
        public int Matrix;
        public string Texture;       // file name
        public bool Alpha;           // blended / alpha tested shader
        public List<Point3D> Positions = new List<Point3D>();
        public List<Vector3D> Normals = new List<Vector3D>();
        public List<System.Windows.Point> UVs = new List<System.Windows.Point>();
        public List<int> Indices = new List<int>();
    }

    public class ShapeModel
    {
        public List<ShapeMatrix> Matrices = new List<ShapeMatrix>();
        public List<ShapeMesh> Meshes = new List<ShapeMesh>();
        public int Triangles;
        public string Error;
        public Rect3D Bounds = Rect3D.Empty;

        /// <summary>Local transform of a node at an animation frame (or the static one).</summary>
        public Matrix3D LocalAt(int i, double frame)
        {
            ShapeMatrix m = Matrices[i];
            if (!m.Animated) return m.Local;
            Matrix3D r = Matrix3D.Identity;
            if (m.Rot.Count > 0) r.Rotate(Interp(m.Rot, frame));
            else { r = m.Local; r.OffsetX = 0; r.OffsetY = 0; r.OffsetZ = 0; }
            Vector3D p = m.Pos.Count > 0 ? InterpP(m.Pos, frame) : new Vector3D(m.Local.OffsetX, m.Local.OffsetY, m.Local.OffsetZ);
            r.OffsetX = p.X; r.OffsetY = p.Y; r.OffsetZ = p.Z;
            return r;
        }

        static Quaternion Interp(List<AnimKey> k, double f)
        {
            if (f <= k[0].Frame) return k[0].Q;
            for (int i = 1; i < k.Count; i++)
                if (f <= k[i].Frame)
                {
                    double t = (f - k[i - 1].Frame) / Math.Max(1e-9, k[i].Frame - k[i - 1].Frame);
                    return Quaternion.Slerp(k[i - 1].Q, k[i].Q, t);
                }
            return k[k.Count - 1].Q;
        }

        static Vector3D InterpP(List<AnimKey> k, double f)
        {
            if (f <= k[0].Frame) return k[0].P;
            for (int i = 1; i < k.Count; i++)
                if (f <= k[i].Frame)
                {
                    double t = (f - k[i - 1].Frame) / Math.Max(1e-9, k[i].Frame - k[i - 1].Frame);
                    return k[i - 1].P + (k[i].P - k[i - 1].P) * t;
                }
            return k[k.Count - 1].P;
        }

        public int Find(string prefix)
        {
            for (int i = 0; i < Matrices.Count; i++) if (Matrices[i].Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }

    public static class ShapeReader
    {
        const int T_SHADER_NAMES = 72, T_POINTS = 7, T_UV_POINTS = 9, T_NORMALS = 5, T_MATRICES = 66,
            T_IMAGES = 14, T_TEXTURES = 16, T_VTX_STATES = 47, T_PRIM_STATES = 55, T_LOD_CONTROLS = 31,
            T_ANIMATIONS = 29, T_HIERARCHY = 67, T_SUB_OBJECTS = 38, T_SUB_OBJECT = 39, T_VERTICES = 50,
            T_VERTEX = 48, T_PRIMITIVES = 53, T_PRIM_STATE_IDX = 56, T_TRILIST = 60, T_VERTEX_IDXS = 63,
            T_DISTANCE_LEVELS = 36, T_DISTANCE_LEVEL = 37, T_TEX_IDXS = 61;

        struct Block { public int Id, Start, End, ContentStart; public string Label; }

        static bool IsBlock(byte[] d, int s, int e)
        {
            if (s + 9 > e) return false;
            int id = BitConverter.ToInt32(d, s), sz = BitConverter.ToInt32(d, s + 4);
            return id > 0 && id < 400 && sz >= 1 && (long)s + 8 + sz <= e;
        }

        static bool Chain(byte[] d, int s, int e)
        {
            if (!IsBlock(d, s, e)) return false;
            while (IsBlock(d, s, e)) s = s + 8 + BitConverter.ToInt32(d, s + 4);
            return s == e;
        }

        static Block Read(byte[] d, int s)
        {
            Block b = new Block();
            b.Id = BitConverter.ToInt32(d, s);
            b.Start = s;
            b.End = s + 8 + BitConverter.ToInt32(d, s + 4);
            int ll = d[s + 8];
            b.Label = ll > 0 ? Encoding.Unicode.GetString(d, s + 9, ll * 2) : "";
            b.ContentStart = s + 9 + ll * 2;
            return b;
        }

        /// <summary>Children of a list block (skips the leading count when there is one).</summary>
        static List<Block> Kids(byte[] d, Block b)
        {
            List<Block> r = new List<Block>();
            int s;
            if (Chain(d, b.ContentStart + 4, b.End)) s = b.ContentStart + 4;
            else if (Chain(d, b.ContentStart, b.End)) s = b.ContentStart;
            else return r;
            while (IsBlock(d, s, b.End)) { Block k = Read(d, s); r.Add(k); s = k.End; }
            return r;
        }

        static Block? Kid(byte[] d, Block b, int id)
        {
            foreach (Block k in Kids(d, b)) if (k.Id == id) return k;
            return null;
        }

        static float F(byte[] d, int o) { return BitConverter.ToSingle(d, o); }
        static int I(byte[] d, int o) { return BitConverter.ToInt32(d, o); }

        static string Str(byte[] d, int o)
        {
            int n = BitConverter.ToUInt16(d, o);
            return Encoding.Unicode.GetString(d, o + 2, n * 2);
        }

        public static ShapeModel Load(string path)
        {
            ShapeModel m = new ShapeModel();
            try
            {
                string kind;
                byte[] d = Simis.ReadPayload(path, out kind);
                int start = 0;
                string head = Encoding.ASCII.GetString(d, 0, Math.Min(16, d.Length));
                if (head.StartsWith("JINX0s1t") || (d.Length > 1 && d[0] == 0xFF) || head.StartsWith("SIMISA")) { TextShape.Load(d, m); ComputeBounds(m); return m; }
                if (head.StartsWith("JINX")) start = 16;
                Block shape = Read(d, start);
                List<Block> top = Kids(d, shape);
                Dictionary<int, Block> t = new Dictionary<int, Block>();
                foreach (Block b in top) if (!t.ContainsKey(b.Id)) t[b.Id] = b;

                List<string> shaders = new List<string>();
                if (t.ContainsKey(T_SHADER_NAMES)) foreach (Block b in Kids(d, t[T_SHADER_NAMES])) shaders.Add(Str(d, b.ContentStart));
                List<Point3D> pts = new List<Point3D>();
                if (t.ContainsKey(T_POINTS)) foreach (Block b in Kids(d, t[T_POINTS])) { int o = b.ContentStart; pts.Add(new Point3D(F(d, o), F(d, o + 4), -F(d, o + 8))); }
                List<System.Windows.Point> uvs = new List<System.Windows.Point>();
                if (t.ContainsKey(T_UV_POINTS)) foreach (Block b in Kids(d, t[T_UV_POINTS])) { int o = b.ContentStart; uvs.Add(new System.Windows.Point(F(d, o), F(d, o + 4))); }
                List<Vector3D> nrm = new List<Vector3D>();
                if (t.ContainsKey(T_NORMALS)) foreach (Block b in Kids(d, t[T_NORMALS])) { int o = b.ContentStart; nrm.Add(new Vector3D(F(d, o), F(d, o + 4), -F(d, o + 8))); }
                if (t.ContainsKey(T_MATRICES))
                    foreach (Block b in Kids(d, t[T_MATRICES]))
                    {
                        int o = b.ContentStart;
                        ShapeMatrix sm = new ShapeMatrix();
                        sm.Name = b.Label;
                        sm.Local = new Matrix3D(F(d, o), F(d, o + 4), -F(d, o + 8), 0,
                                                F(d, o + 12), F(d, o + 16), -F(d, o + 20), 0,
                                                -F(d, o + 24), -F(d, o + 28), F(d, o + 32), 0,
                                                F(d, o + 36), F(d, o + 40), -F(d, o + 44), 1);
                        m.Matrices.Add(sm);
                    }
                List<string> images = new List<string>();
                if (t.ContainsKey(T_IMAGES)) foreach (Block b in Kids(d, t[T_IMAGES])) images.Add(Str(d, b.ContentStart));
                List<int> texImage = new List<int>();
                if (t.ContainsKey(T_TEXTURES)) foreach (Block b in Kids(d, t[T_TEXTURES])) texImage.Add(I(d, b.ContentStart));
                List<int> vtxMatrix = new List<int>();
                if (t.ContainsKey(T_VTX_STATES)) foreach (Block b in Kids(d, t[T_VTX_STATES])) vtxMatrix.Add(I(d, b.ContentStart + 4));
                // prim state -> (vtx state, texture, shader)
                List<int[]> prim = new List<int[]>();
                if (t.ContainsKey(T_PRIM_STATES))
                    foreach (Block b in Kids(d, t[T_PRIM_STATES]))
                    {
                        int o = b.ContentStart;
                        int shader = I(d, o + 4);
                        int tex = -1, after = o + 8;
                        if (IsBlock(d, o + 8, b.End) && I(d, o + 8) == T_TEX_IDXS)
                        {
                            Block ti = Read(d, o + 8);
                            int cnt = I(d, ti.ContentStart);
                            if (cnt > 0) tex = I(d, ti.ContentStart + 4);
                            after = ti.End;
                        }
                        int vtx = I(d, after + 4); // after zbias(float)
                        prim.Add(new int[] { vtx, tex, shader });
                    }

                if (t.ContainsKey(T_LOD_CONTROLS))
                {
                    Block lod = Kids(d, t[T_LOD_CONTROLS])[0];
                    Block? dls = Kid(d, lod, T_DISTANCE_LEVELS);
                    if (dls.HasValue)
                    {
                        List<Block> levels = Kids(d, dls.Value);
                        Block level = levels[0];
                        foreach (Block x in Kids(d, level))
                        {
                            if (x.Id == 34) // distance_level_header -> hierarchy
                            {
                                Block? h = Kid(d, x, T_HIERARCHY);
                                if (h.HasValue)
                                {
                                    int cnt = I(d, h.Value.ContentStart);
                                    for (int i = 0; i < cnt && i < m.Matrices.Count; i++) m.Matrices[i].Parent = I(d, h.Value.ContentStart + 4 + i * 4);
                                }
                            }
                            if (x.Id == T_SUB_OBJECTS)
                                foreach (Block so in Kids(d, x)) ReadSubObject(d, so, pts, uvs, nrm, prim, vtxMatrix, texImage, images, shaders, m);
                        }
                    }
                }
                if (t.ContainsKey(T_ANIMATIONS)) ReadAnimations(d, t[T_ANIMATIONS], m);
                ComputeBounds(m);
            }
            catch (Exception ex) { m.Error = "Shape could not be read: " + ex.Message; }
            return m;
        }

        static void ReadSubObject(byte[] d, Block so, List<Point3D> pts, List<System.Windows.Point> uvs, List<Vector3D> nrm,
            List<int[]> prim, List<int> vtxMatrix, List<int> texImage, List<string> images, List<string> shaders, ShapeModel m)
        {
            List<int[]> verts = new List<int[]>(); // point, normal, uv
            Block? vb = Kid(d, so, T_VERTICES);
            if (!vb.HasValue) return;
            foreach (Block v in Kids(d, vb.Value))
            {
                int o = v.ContentStart;
                int p = I(d, o + 4), n = I(d, o + 8), uv = -1;
                int uo = o + 20;
                if (IsBlock(d, uo, v.End)) { Block ub = Read(d, uo); if (I(d, ub.ContentStart) > 0) uv = I(d, ub.ContentStart + 4); }
                verts.Add(new int[] { p, n, uv });
            }
            Block? pb = Kid(d, so, T_PRIMITIVES);
            if (!pb.HasValue) return;
            int curPrim = 0;
            foreach (Block x in Kids(d, pb.Value))
            {
                if (x.Id == T_PRIM_STATE_IDX) { curPrim = I(d, x.ContentStart); continue; }
                if (x.Id != T_TRILIST) continue;
                Block? vi = Kid(d, x, T_VERTEX_IDXS);
                if (!vi.HasValue) continue;
                int cnt = I(d, vi.Value.ContentStart);
                int[] ps = curPrim < prim.Count ? prim[curPrim] : new int[] { 0, -1, 0 };
                int matrix = ps[0] >= 0 && ps[0] < vtxMatrix.Count ? vtxMatrix[ps[0]] : 0;
                if (matrix < 0 || matrix >= m.Matrices.Count) matrix = 0;
                string tex = null;
                if (ps[1] >= 0 && ps[1] < texImage.Count && texImage[ps[1]] >= 0 && texImage[ps[1]] < images.Count) tex = images[texImage[ps[1]]];
                string shader = ps[2] >= 0 && ps[2] < shaders.Count ? shaders[ps[2]] : "";
                ShapeMesh mesh = new ShapeMesh();
                mesh.Matrix = matrix; mesh.Texture = tex;
                mesh.Alpha = shader.IndexOf("Blend", StringComparison.OrdinalIgnoreCase) >= 0;
                Dictionary<int, int> remap = new Dictionary<int, int>();
                int o = vi.Value.ContentStart + 4;
                for (int i = 0; i < cnt; i++)
                {
                    int vIdx = I(d, o + i * 4);
                    int ni;
                    if (!remap.TryGetValue(vIdx, out ni))
                    {
                        ni = mesh.Positions.Count;
                        remap[vIdx] = ni;
                        int[] vv = vIdx >= 0 && vIdx < verts.Count ? verts[vIdx] : new int[] { 0, 0, -1 };
                        mesh.Positions.Add(vv[0] >= 0 && vv[0] < pts.Count ? pts[vv[0]] : new Point3D());
                        mesh.Normals.Add(vv[1] >= 0 && vv[1] < nrm.Count ? nrm[vv[1]] : new Vector3D(0, 1, 0));
                        mesh.UVs.Add(vv[2] >= 0 && vv[2] < uvs.Count ? uvs[vv[2]] : new System.Windows.Point());
                    }
                    mesh.Indices.Add(ni);
                }
                // MSTS winding is clockwise; flip because Z was mirrored
                for (int i = 0; i + 2 < mesh.Indices.Count; i += 3) { int tmp = mesh.Indices[i + 1]; mesh.Indices[i + 1] = mesh.Indices[i + 2]; mesh.Indices[i + 2] = tmp; }
                m.Triangles += cnt / 3;
                m.Meshes.Add(mesh);
            }
        }

        static void ReadAnimations(byte[] d, Block anims, ShapeModel m)
        {
            foreach (Block anim in Kids(d, anims))
            {
                // animation: frames(int) fps(int) anim_nodes( n anim_node(...)... )
                int s = anim.ContentStart + 8;
                if (!IsBlock(d, s, anim.End)) continue;
                Block nodes = Read(d, s);
                foreach (Block node in Kids(d, nodes))
                {
                    int mi = -1;
                    for (int i = 0; i < m.Matrices.Count; i++) if (m.Matrices[i].Name == node.Label) { mi = i; break; }
                    if (mi < 0) continue;
                    foreach (Block ctrls in Kids(d, node))
                        foreach (Block ctrl in Kids(d, ctrls))
                            foreach (Block key in Kids(d, ctrl))
                            {
                                int o = key.ContentStart, len = key.End - key.ContentStart;
                                AnimKey k = new AnimKey();
                                k.Frame = I(d, o);
                                if (len >= 20 && len != 36)
                                {
                                    k.Q = new Quaternion(F(d, o + 4), F(d, o + 8), -F(d, o + 12), F(d, o + 16));
                                    m.Matrices[mi].Rot.Add(k);
                                }
                                else if (len == 16 || len == 36)
                                {
                                    k.P = new Vector3D(F(d, o + 4), F(d, o + 8), -F(d, o + 12));
                                    m.Matrices[mi].Pos.Add(k);
                                }
                            }
                    m.Matrices[mi].Rot.Sort(delegate(AnimKey a, AnimKey b) { return a.Frame.CompareTo(b.Frame); });
                    m.Matrices[mi].Pos.Sort(delegate(AnimKey a, AnimKey b) { return a.Frame.CompareTo(b.Frame); });
                }
            }
        }

        internal static void ComputeBounds(ShapeModel m)
        {
            Matrix3D[] world = World(m, null);
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (ShapeMesh mesh in m.Meshes)
            {
                Matrix3D w = world[mesh.Matrix];
                for (int i = 0; i < mesh.Positions.Count; i += 3)
                {
                    Point3D p = w.Transform(mesh.Positions[i]);
                    if (p.X < minX) minX = p.X; if (p.Y < minY) minY = p.Y; if (p.Z < minZ) minZ = p.Z;
                    if (p.X > maxX) maxX = p.X; if (p.Y > maxY) maxY = p.Y; if (p.Z > maxZ) maxZ = p.Z;
                }
            }
            if (minX <= maxX) m.Bounds = new Rect3D(minX, minY, minZ, maxX - minX, maxY - minY, maxZ - minZ);
        }

        /// <summary>World transforms for every node. locals may override node local matrices (animation).</summary>
        public static Matrix3D[] World(ShapeModel m, Matrix3D[] locals)
        {
            int n = m.Matrices.Count;
            Matrix3D[] w = new Matrix3D[Math.Max(1, n)];
            bool[] done = new bool[w.Length];
            if (n == 0) { w[0] = Matrix3D.Identity; return w; }
            for (int i = 0; i < n; i++) Compute(m, locals, w, done, i, 0);
            return w;
        }

        static Matrix3D Compute(ShapeModel m, Matrix3D[] locals, Matrix3D[] w, bool[] done, int i, int depth)
        {
            if (done[i]) return w[i];
            Matrix3D local = locals != null ? locals[i] : m.Matrices[i].Local;
            int p = m.Matrices[i].Parent;
            if (p >= 0 && p < m.Matrices.Count && p != i && depth < 64) local = local * Compute(m, locals, w, done, p, depth + 1);
            w[i] = local; done[i] = true;
            return local;
        }
    }

    public static class AceReader
    {
        static readonly Dictionary<string, BitmapSource> cache = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);

        public static BitmapSource LoadCached(string path, int maxSize)
        {
            string key = path + "|" + maxSize;
            lock (cache) { BitmapSource b; if (cache.TryGetValue(key, out b)) return b; }
            BitmapSource r = Load(path, maxSize);
            lock (cache) cache[key] = r;
            return r;
        }

        /// <summary>Decodes an .ace (plain or compressed; DXT1 or per-channel rows) to a frozen BGRA bitmap.</summary>
        public static BitmapSource Load(string path, int maxSize)
        {
            try
            {
                string kind;
                byte[] d = Simis.ReadPayload(path, out kind);
                int opt = BitConverter.ToInt32(d, 4), w = BitConverter.ToInt32(d, 8), h = BitConverter.ToInt32(d, 12);
                int fmt = BitConverter.ToInt32(d, 16), ch = BitConverter.ToInt32(d, 20);
                bool mips = (opt & 1) != 0, raw = (opt & 16) != 0;
                if (w <= 0 || h <= 0 || w > 8192 || h > 8192) return null;
                int level = 0;
                if (mips) while ((w >> level) > maxSize && (w >> level) > 4 && (h >> level) > 4) level++;
                int lw = Math.Max(1, w >> level), lh = Math.Max(1, h >> level);
                int[] chBits = new int[ch], chType = new int[ch];
                for (int i = 0; i < ch; i++) { chBits[i] = (int)BitConverter.ToInt64(d, 152 + i * 16); chType[i] = (int)BitConverter.ToInt64(d, 160 + i * 16); }
                int tableStart = 152 + ch * 16;
                byte[] px = new byte[lw * lh * 4];
                bool hasAlpha = false;
                if (raw)
                {
                    int off = BitConverter.ToInt32(d, tableStart + level * 4);
                    int dataStart = off + 4;
                    if (fmt == 0x12) DecodeDxt1(d, dataStart, lw, lh, px, out hasAlpha);
                    else return null;
                }
                else
                {
                    int rowTable = tableStart;
                    for (int l = 0; l < level; l++) rowTable += Math.Max(1, h >> l) * 4;
                    for (int y = 0; y < lh; y++)
                    {
                        int o = BitConverter.ToInt32(d, rowTable + y * 4);
                        for (int c = 0; c < ch; c++)
                        {
                            int type = chType[c];
                            if (chBits[c] == 1)
                            {
                                for (int x = 0; x < lw; x++)
                                {
                                    bool on = (d[o + (x >> 3)] & (0x80 >> (x & 7))) != 0;
                                    if (type == 2) { px[(y * lw + x) * 4 + 3] = on ? (byte)255 : (byte)0; if (!on) hasAlpha = true; }
                                }
                                o += (lw + 7) / 8;
                            }
                            else
                            {
                                int dst = type == 3 ? 2 : type == 4 ? 1 : type == 5 ? 0 : type == 6 ? 3 : -1;
                                if (dst >= 0) for (int x = 0; x < lw; x++) px[(y * lw + x) * 4 + dst] = d[o + x];
                                if (type == 6) hasAlpha = true;
                                o += lw;
                            }
                        }
                    }
                    bool anyAlphaChannel = false;
                    foreach (int t in chType) if (t == 2 || t == 6) anyAlphaChannel = true;
                    if (!anyAlphaChannel) for (int i = 3; i < px.Length; i += 4) px[i] = 255;
                }
                // premultiply for Pbgra32
                if (hasAlpha)
                    for (int i = 0; i < px.Length; i += 4)
                    {
                        int a = px[i + 3];
                        if (a == 255) continue;
                        px[i] = (byte)(px[i] * a / 255); px[i + 1] = (byte)(px[i + 1] * a / 255); px[i + 2] = (byte)(px[i + 2] * a / 255);
                    }
                BitmapSource bmp = BitmapSource.Create(lw, lh, 96, 96, PixelFormats.Pbgra32, null, px, lw * 4);
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        static void DecodeDxt1(byte[] d, int o, int w, int h, byte[] px, out bool alpha)
        {
            alpha = false;
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
            byte[] r = new byte[4], g = new byte[4], b = new byte[4], a = new byte[4];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    if (o + 8 > d.Length) return;
                    int c0 = BitConverter.ToUInt16(d, o), c1 = BitConverter.ToUInt16(d, o + 2);
                    uint bits = BitConverter.ToUInt32(d, o + 4);
                    o += 8;
                    Unpack(c0, out r[0], out g[0], out b[0]); Unpack(c1, out r[1], out g[1], out b[1]);
                    a[0] = a[1] = a[2] = 255; a[3] = 255;
                    if (c0 > c1)
                    {
                        r[2] = (byte)((2 * r[0] + r[1]) / 3); g[2] = (byte)((2 * g[0] + g[1]) / 3); b[2] = (byte)((2 * b[0] + b[1]) / 3);
                        r[3] = (byte)((r[0] + 2 * r[1]) / 3); g[3] = (byte)((g[0] + 2 * g[1]) / 3); b[3] = (byte)((b[0] + 2 * b[1]) / 3);
                    }
                    else
                    {
                        r[2] = (byte)((r[0] + r[1]) / 2); g[2] = (byte)((g[0] + g[1]) / 2); b[2] = (byte)((b[0] + b[1]) / 2);
                        r[3] = g[3] = b[3] = 0; a[3] = 0;
                    }
                    for (int py = 0; py < 4; py++)
                        for (int pxx = 0; pxx < 4; pxx++)
                        {
                            int x = bx * 4 + pxx, y = by * 4 + py;
                            int idx = (int)((bits >> (2 * (py * 4 + pxx))) & 3);
                            if (x >= w || y >= h) continue;
                            int p = (y * w + x) * 4;
                            px[p] = b[idx]; px[p + 1] = g[idx]; px[p + 2] = r[idx]; px[p + 3] = a[idx];
                            if (a[idx] == 0) alpha = true;
                        }
                }
        }

        static void Unpack(int c, out byte r, out byte g, out byte b)
        {
            r = (byte)(((c >> 11) & 31) * 255 / 31);
            g = (byte)(((c >> 5) & 63) * 255 / 63);
            b = (byte)((c & 31) * 255 / 31);
        }
    }
}
