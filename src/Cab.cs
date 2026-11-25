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
            string day = DayImage(view);
            if (day == null) return null;
            if (cabLight) { string p = Variant(day, "CABLIGHT"); if (p != null) return p; }
            if (night || cabLight) { string p = Variant(day, "NIGHT"); if (p != null) return p; }
            return File.Exists(day) ? day : null;
        }

        /// <summary>Full path of a view's day image (the .cvf may point into another folder, e.g. ..\CABVIEW\front.ace).</summary>
        string DayImage(int view)
        {
            if (view < 0 || view >= Views.Count || string.IsNullOrEmpty(Views[view].Image)) return null;
            try { return System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), Views[view].Image)); }
            catch { return null; }
        }

        /// <summary>Open Rails looks for the lit / night image in a CABLIGHT / NIGHT folder next to the day image.</summary>
        static string Variant(string dayImage, string folder)
        {
            string p = System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dayImage), folder), System.IO.Path.GetFileName(dayImage));
            return File.Exists(p) ? p : null;
        }

        /// <summary>True when this view has a CABLIGHT image (the cab light visibly turns on and off).</summary>
        public bool HasCabLight(int view)
        {
            string day = DayImage(view);
            return day != null && Variant(day, "CABLIGHT") != null;
        }

        /// <summary>True when any view of this cab has a CABLIGHT image.</summary>
        public bool HasAnyCabLight()
        {
            for (int i = 0; i < Views.Count; i++) if (HasCabLight(i)) return true;
            return false;
        }
    }
}
