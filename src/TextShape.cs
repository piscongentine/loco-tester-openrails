// Loco Tester - reader for text-format MSTS shapes (JINX0s1t), same model as the binary reader.
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Media.Media3D;

namespace LocoTester
{
    static class TextShape
    {
        static readonly HashSet<string> labelled = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "matrix", "prim_state", "anim_node", "named_shader", "named_filter_mode" };

        public static void Load(byte[] d, ShapeModel m)
        {
            Encoding enc;
            string text = Stf.DecodeText(d, out enc);
            if (text.StartsWith("JINX", StringComparison.Ordinal)) { int nl = text.IndexOf('\n'); text = nl < 0 ? "" : text.Substring(nl + 1); }
            StfNode root = Stf.Parse(text);
            FixLabels(root);
            StfNode shape = root.Child("shape");
            if (shape == null) { m.Error = "Shape file has no shape block"; return; }

            List<string> shaders = new List<string>();
            StfNode sn = shape.Child("shader_names");
            if (sn != null) foreach (StfNode s in sn.Children()) shaders.Add(s.Label ?? s.Value(0) ?? "");
            List<Point3D> pts = new List<Point3D>();
            StfNode pn = shape.Child("points");
            if (pn != null) foreach (StfNode p in pn.Children("point")) pts.Add(new Point3D(p.Num(0, 0), p.Num(1, 0), -p.Num(2, 0)));
            List<System.Windows.Point> uvs = new List<System.Windows.Point>();
            StfNode un = shape.Child("uv_points");
            if (un != null) foreach (StfNode p in un.Children("uv_point")) uvs.Add(new System.Windows.Point(p.Num(0, 0), p.Num(1, 0)));
            List<Vector3D> nrm = new List<Vector3D>();
            StfNode nn = shape.Child("normals");
            if (nn != null) foreach (StfNode p in nn.Children("vector")) nrm.Add(new Vector3D(p.Num(0, 0), p.Num(1, 0), -p.Num(2, 0)));
            StfNode mn = shape.Child("matrices");
            if (mn != null)
                foreach (StfNode x in mn.Children("matrix"))
                {
                    ShapeMatrix sm = new ShapeMatrix();
                    sm.Name = x.Label ?? "";
                    sm.Local = new Matrix3D(x.Num(0, 1), x.Num(1, 0), -x.Num(2, 0), 0,
                                            x.Num(3, 0), x.Num(4, 1), -x.Num(5, 0), 0,
                                            -x.Num(6, 0), -x.Num(7, 0), x.Num(8, 1), 0,
                                            x.Num(9, 0), x.Num(10, 0), -x.Num(11, 0), 1);
                    m.Matrices.Add(sm);
                }
            List<string> images = new List<string>();
            StfNode im = shape.Child("images");
            if (im != null) foreach (StfNode x in im.Children("image")) images.Add(x.Value(0) ?? "");
            List<int> texImage = new List<int>();
            StfNode tx = shape.Child("textures");
            if (tx != null) foreach (StfNode x in tx.Children("texture")) texImage.Add((int)x.Num(0, -1));
            List<int> vtxMatrix = new List<int>();
            StfNode vs = shape.Child("vtx_states");
            if (vs != null) foreach (StfNode x in vs.Children("vtx_state")) vtxMatrix.Add((int)x.Num(1, 0));
            List<int[]> prim = new List<int[]>();
            StfNode ps = shape.Child("prim_states");
            if (ps != null)
                foreach (StfNode x in ps.Children("prim_state"))
                {
                    // flags shader tex_idxs( n i.. ) zbias vtx_state alphatest lightcfg zbufmode
                    List<string> v = x.Values();
                    int shader = v.Count > 1 ? (int)Stf.ParseNumber(v[1], 0) : 0;
                    int vtx = v.Count > 3 ? (int)Stf.ParseNumber(v[3], 0) : 0;
                    int tex = -1;
                    StfNode ti = x.Child("tex_idxs");
                    if (ti != null && ti.Num(0, 0) > 0) tex = (int)ti.Num(1, -1);
                    prim.Add(new int[] { vtx, tex, shader });
                }

            StfNode lods = shape.Child("lod_controls");
            StfNode lod = lods != null ? lods.Child("lod_control") : null;
            StfNode dls = lod != null ? lod.Child("distance_levels") : null;
            StfNode level = dls != null ? dls.Child("distance_level") : null;
            if (level != null)
            {
                StfNode hdr = level.Child("distance_level_header");
                StfNode h = hdr != null ? hdr.Child("hierarchy") : null;
                if (h != null)
                {
                    List<string> hv = h.Values();
                    for (int i = 1; i < hv.Count && i - 1 < m.Matrices.Count; i++) m.Matrices[i - 1].Parent = (int)Stf.ParseNumber(hv[i], -1);
                }
                StfNode sos = level.Child("sub_objects");
                if (sos != null) foreach (StfNode so in sos.Children("sub_object")) SubObject(so, pts, uvs, nrm, prim, vtxMatrix, texImage, images, shaders, m);
            }

            StfNode anims = shape.Child("animations");
            if (anims != null)
                foreach (StfNode anim in anims.Children("animation"))
                {
                    StfNode nodes = anim.Child("anim_nodes");
                    if (nodes == null) continue;
                    foreach (StfNode node in nodes.Children("anim_node"))
                    {
                        int mi = -1;
                        for (int i = 0; i < m.Matrices.Count; i++) if (m.Matrices[i].Name == node.Label) { mi = i; break; }
                        if (mi < 0) continue;
                        StfNode ctrls = node.Child("controllers");
                        if (ctrls == null) continue;
                        foreach (StfNode c in ctrls.Children())
                            foreach (StfNode k in c.Children())
                            {
                                AnimKey key = new AnimKey();
                                key.Frame = k.Num(0, 0);
                                string kn = k.Name.ToLowerInvariant();
                                if (kn == "slerp_rot" || kn == "tcb_key")
                                {
                                    key.Q = new Quaternion(k.Num(1, 0), k.Num(2, 0), -k.Num(3, 0), k.Num(4, 1));
                                    m.Matrices[mi].Rot.Add(key);
                                }
                                else if (kn == "linear_key" || kn == "tcb_pos_key")
                                {
                                    key.P = new Vector3D(k.Num(1, 0), k.Num(2, 0), -k.Num(3, 0));
                                    m.Matrices[mi].Pos.Add(key);
                                }
                            }
                        m.Matrices[mi].Rot.Sort(delegate(AnimKey a, AnimKey b) { return a.Frame.CompareTo(b.Frame); });
                        m.Matrices[mi].Pos.Sort(delegate(AnimKey a, AnimKey b) { return a.Frame.CompareTo(b.Frame); });
                    }
                }
        }

        static void SubObject(StfNode so, List<Point3D> pts, List<System.Windows.Point> uvs, List<Vector3D> nrm, List<int[]> prim,
            List<int> vtxMatrix, List<int> texImage, List<string> images, List<string> shaders, ShapeModel m)
        {
            List<int[]> verts = new List<int[]>();
            StfNode vn = so.Child("vertices");
            if (vn == null) return;
            foreach (StfNode v in vn.Children("vertex"))
            {
                int uv = -1;
                StfNode vu = v.Child("vertex_uvs");
                if (vu != null && vu.Num(0, 0) > 0) uv = (int)vu.Num(1, -1);
                verts.Add(new int[] { (int)v.Num(1, 0), (int)v.Num(2, 0), uv });
            }
            StfNode pr = so.Child("primitives");
            if (pr == null) return;
            int cur = 0;
            foreach (StfNode x in pr.Children())
            {
                string n = x.Name.ToLowerInvariant();
                if (n == "prim_state_idx") { cur = (int)x.Num(0, 0); continue; }
                if (n != "indexed_trilist") continue;
                StfNode vi = x.Child("vertex_idxs");
                if (vi == null) continue;
                List<string> idx = vi.Values();
                int[] p = cur < prim.Count ? prim[cur] : new int[] { 0, -1, 0 };
                ShapeMesh mesh = new ShapeMesh();
                mesh.Matrix = p[0] >= 0 && p[0] < vtxMatrix.Count ? vtxMatrix[p[0]] : 0;
                if (mesh.Matrix < 0 || mesh.Matrix >= m.Matrices.Count) mesh.Matrix = 0;
                if (p[1] >= 0 && p[1] < texImage.Count && texImage[p[1]] >= 0 && texImage[p[1]] < images.Count) mesh.Texture = images[texImage[p[1]]];
                string shader = p[2] >= 0 && p[2] < shaders.Count ? shaders[p[2]] : "";
                mesh.Alpha = shader.IndexOf("Blend", StringComparison.OrdinalIgnoreCase) >= 0;
                Dictionary<int, int> remap = new Dictionary<int, int>();
                for (int i = 1; i < idx.Count; i++)
                {
                    int vIdx = (int)Stf.ParseNumber(idx[i], 0);
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
                int whole = mesh.Indices.Count - mesh.Indices.Count % 3;
                if (whole < mesh.Indices.Count) mesh.Indices.RemoveRange(whole, mesh.Indices.Count - whole);
                for (int i = 0; i + 2 < mesh.Indices.Count; i += 3) { int t = mesh.Indices[i + 1]; mesh.Indices[i + 1] = mesh.Indices[i + 2]; mesh.Indices[i + 2] = t; }
                m.Triangles += mesh.Indices.Count / 3;
                m.Meshes.Add(mesh);
            }
        }

        /// <summary>"matrix MAIN ( ... )" parses as value "matrix" + node "MAIN"; join them.</summary>
        static void FixLabels(StfNode n)
        {
            for (int i = 0; i + 1 < n.Items.Count; i++)
            {
                string s = n.Items[i] as string;
                StfNode next = n.Items[i + 1] as StfNode;
                if (s != null && next != null && labelled.Contains(s))
                {
                    next.Label = next.Name;
                    next.Name = s;
                    n.Items.RemoveAt(i);
                }
            }
            foreach (StfNode c in n.Children()) FixLabels(c);
        }
    }
}
