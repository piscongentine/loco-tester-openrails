// Loco Tester - 2D cab view (.cvf) reader.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LocoTester
{
    public class CabViewDef
    {
        public string Image;           // file name inside CABVIEW
        public double X, Y, Z;         // eye position (MSTS coords)
        public double Pitch, Heading;  // degrees
    }

    public class CabControl
    {
        public string Kind;            // Dial, Lever, TwoState ...
        public string Type;            // e.g. HORN, THROTTLE LEVER
        public double X, Y, W, H;      // 640x480 cab coordinates
    }

    public class CabFile
    {
        public string Path;
        public List<CabViewDef> Views = new List<CabViewDef>();
        public List<CabControl> Controls = new List<CabControl>();
        public string Error;

        public static CabFile Load(string path)
        {
            CabFile c = new CabFile();
            c.Path = path;
            try
            {
                Encoding enc;
                StfNode root = Stf.Parse(Stf.ReadText(path, out enc));
                StfNode cv = root.Child("Tr_CabViewFile");
                if (cv == null) { c.Error = "Not a cab view file"; return c; }
                CabViewDef cur = null;
                foreach (StfNode n in cv.Children())
                {
                    string nm = n.Name.ToLowerInvariant();
                    if (nm == "cabviewfile") { cur = new CabViewDef(); cur.Image = n.Value(0); c.Views.Add(cur); }
                    else if (nm == "position" && cur != null) { cur.X = n.Num(0, 0); cur.Y = n.Num(1, 0); cur.Z = n.Num(2, 0); }
                    else if (nm == "direction" && cur != null) { cur.Pitch = n.Num(0, 0); cur.Heading = n.Num(1, 0); }
                    else if (nm == "cabviewcontrols")
                    {
                        foreach (StfNode ctl in n.Children())
                        {
                            CabControl cc = new CabControl();
                            cc.Kind = ctl.Name;
                            StfNode t = ctl.Child("Type");
                            cc.Type = t != null ? string.Join(" ", t.Values().ToArray()).ToUpperInvariant() : "";
                            StfNode p = ctl.Child("Position");
                            if (p == null) continue;
                            cc.X = p.Num(0, 0); cc.Y = p.Num(1, 0); cc.W = p.Num(2, 0); cc.H = p.Num(3, 0);
                            c.Controls.Add(cc);
                        }
                    }
                }
            }
            catch (Exception ex) { c.Error = ex.Message; }
            return c;
        }

        /// <summary>Image for a view, optionally the NIGHT or CABLIGHT version.</summary>
        public string ImagePath(int view, bool night, bool cabLight)
        {
            if (view < 0 || view >= Views.Count || Views[view].Image == null) return null;
            string dir = System.IO.Path.GetDirectoryName(Path);
            string img = Views[view].Image;
            if (cabLight)
            {
                string p = System.IO.Path.Combine(System.IO.Path.Combine(dir, "CABLIGHT"), img);
                if (File.Exists(p)) return p;
            }
            if (night || cabLight)
            {
                string p = System.IO.Path.Combine(System.IO.Path.Combine(dir, "NIGHT"), img);
                if (File.Exists(p)) return p;
            }
            string d = System.IO.Path.Combine(dir, img);
            return File.Exists(d) ? d : null;
        }

        public bool HasCabLight(int view)
        {
            if (view < 0 || view >= Views.Count || Views[view].Image == null) return false;
            return File.Exists(System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), "CABLIGHT"), Views[view].Image));
        }
    }
}
