// Loco Tester - locomotive (.eng) model and fast folder scanner.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace LocoTester
{
    public class LightDef
    {
        public int Type;            // 0 = glow, 1 = cone/beam
        public int Headlight;       // 0 ignore, 1 off, 2 dim, 3 bright
        public int Unit;            // 0 ignore, 1 middle, 2 first, 3 first rev, 4 last, 5 last rev
        public int Penalty, Control, Service, TimeOfDay, Weather, Coupling;
        public string Comment = "";
        public List<LightState> States = new List<LightState>();
    }

    public class LightState
    {
        public uint Colour = 0xFFFFFFFF;
        public double X, Y, Z;       // MSTS coordinates
        public double Radius = 0.5;
        public double Duration;
    }

    /// <summary>Smoke / steam outlet from the .eng Effects block (Exhaust1, StackFX, WhistleFX ...).</summary>
    public class Emitter
    {
        public string Name;          // Exhaust1, StackFX, WhistleFX, CylindersFX ...
        public bool Steam;           // from SteamSpecialEffects
        public double X, Y, Z;       // MSTS coordinates
        public double DX, DY = 1, DZ;
        public double Width = 0.4;
    }

    public class SoundRef
    {
        public string FileName;     // as written in the .eng
        public string Section;      // "Wagon" or "Engine"
        public string ResolvedPath; // null if not found
    }

    public class Loco
    {
        public string EngPath;
        public string Folder;
        public string FolderName;
        public string DisplayName;
        public string WagonName;
        public string EngineType = "Unknown";   // Electric / Diesel / Steam
        public string ShapeFile;
        public string ShapePath;
        public double WheelRadius = 0.5;
        public double MaxSpeedMps = 0;
        public double MaxPowerW = 0;
        public double MassKg = 0;
        public List<string> CabViews = new List<string>();
        public List<SoundRef> Sounds = new List<SoundRef>();
        public List<LightDef> Lights = new List<LightDef>();
        public List<Emitter> Emitters = new List<Emitter>();
        public double SmokeIdleRate = 0.2, SmokeMaxRate = 20;
        public string ParseError;
        public Encoding Encoding;
        public DateTime Modified;

        public string TrainsetRoot { get { return Path.GetDirectoryName(Folder); } }

        public static Loco Load(string engPath, string orRoot)
        {
            Loco l = new Loco();
            l.EngPath = engPath;
            l.Folder = Path.GetDirectoryName(engPath);
            l.FolderName = Path.GetFileName(l.Folder);
            l.Modified = File.GetLastWriteTime(engPath);
            try
            {
                string text = Stf.ReadText(engPath, out l.Encoding);
                StfNode root = Stf.Parse(text);
                StfNode wagon = root.Child("Wagon");
                StfNode engine = root.Child("Engine");
                if (wagon == null && engine == null) { l.ParseError = "No Wagon/Engine section"; }
                if (wagon != null)
                {
                    l.WagonName = wagon.Value(0);
                    StfNode ws = wagon.Child("WagonShape");
                    if (ws != null) l.ShapeFile = ws.Value(0);
                    StfNode wr = wagon.Child("WheelRadius");
                    if (wr != null) l.WheelRadius = Stf.ParseUnit(wr.Value(0), 0.5);
                    StfNode mass = wagon.Child("Mass");
                    if (mass != null) l.MassKg = Stf.ParseUnit(mass.Value(0), 0) * (mass.Value(0) != null && mass.Value(0).ToLowerInvariant().Contains("t") ? 1000 : 1);
                    foreach (StfNode s in wagon.Children("Sound")) l.AddSound(s.Value(0), "Wagon", orRoot);
                    StfNode lights = wagon.Child("Lights");
                    if (lights != null) l.ReadLights(lights);
                }
                if (engine != null)
                {
                    StfNode t = engine.Child("Type");
                    if (t != null && t.Value(0) != null) l.EngineType = t.Value(0);
                    StfNode n = engine.Child("Name");
                    if (n != null) l.DisplayName = n.Value(0);
                    StfNode mv = engine.Child("MaxVelocity");
                    if (mv != null) l.MaxSpeedMps = Stf.ParseUnit(mv.Value(0), 0);
                    StfNode mp = engine.Child("MaxPower");
                    if (mp != null) l.MaxPowerW = Stf.ParseUnit(mp.Value(0), 0);
                    StfNode wr = engine.Child("WheelRadius");
                    if (wr != null) l.WheelRadius = Stf.ParseUnit(wr.Value(0), l.WheelRadius);
                    foreach (StfNode cv in engine.Children("CabView")) if (cv.Value(0) != null) l.CabViews.Add(cv.Value(0));
                    foreach (StfNode s in engine.Children("Sound")) l.AddSound(s.Value(0), "Engine", orRoot);
                }
                foreach (StfNode fx in root.Find("Effects"))
                    foreach (StfNode grp in fx.Children())
                    {
                        bool steam = grp.Name.IndexOf("Steam", StringComparison.OrdinalIgnoreCase) >= 0;
                        foreach (StfNode e in grp.Children())
                        {
                            List<string> v = e.Values();
                            if (v.Count < 7) continue;
                            Emitter em = new Emitter();
                            em.Name = e.Name; em.Steam = steam;
                            em.X = Stf.ParseNumber(v[0], 0); em.Y = Stf.ParseNumber(v[1], 0); em.Z = Stf.ParseNumber(v[2], 0);
                            em.DX = Stf.ParseNumber(v[3], 0); em.DY = Stf.ParseNumber(v[4], 1); em.DZ = Stf.ParseNumber(v[5], 0);
                            em.Width = Math.Max(0.05, Stf.ParseUnit(v[6], 0.4));
                            l.Emitters.Add(em);
                        }
                    }
                foreach (StfNode n in root.Find("DieselSmokeEffectInitialSmokeRate")) l.SmokeIdleRate = n.Num(0, 0.2);
                foreach (StfNode n in root.Find("DieselSmokeEffectMaxSmokeRate")) l.SmokeMaxRate = n.Num(0, 20);
                if (string.IsNullOrEmpty(l.DisplayName)) l.DisplayName = l.WagonName ?? Path.GetFileNameWithoutExtension(engPath);
                if (l.ShapeFile != null)
                {
                    string sp = Path.Combine(l.Folder, l.ShapeFile);
                    l.ShapePath = File.Exists(sp) ? sp : null;
                }
            }
            catch (Exception ex)
            {
                l.ParseError = ex.Message;
                if (l.DisplayName == null) l.DisplayName = Path.GetFileNameWithoutExtension(engPath);
            }
            return l;
        }

        void AddSound(string file, string section, string orRoot)
        {
            if (string.IsNullOrEmpty(file)) return;
            SoundRef r = new SoundRef();
            r.FileName = file;
            r.Section = section;
            r.ResolvedPath = ResolveSms(Folder, file, orRoot);
            Sounds.Add(r);
        }

        /// <summary>Open Rails looks for .sms in the trainset's SOUND folder, then the global SOUND folder.</summary>
        public static string ResolveSms(string locoFolder, string file, string orRoot)
        {
            if (string.IsNullOrEmpty(file) || file.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
            string a = Path.Combine(Path.Combine(locoFolder, "SOUND"), file);
            if (File.Exists(a)) return a;
            if (!string.IsNullOrEmpty(orRoot))
            {
                string b = Path.Combine(Path.Combine(orRoot, "SOUND"), file);
                if (File.Exists(b)) return b;
            }
            return null;
        }

        void ReadLights(StfNode lights)
        {
            foreach (StfNode ln in lights.Children("Light"))
            {
                LightDef d = new LightDef();
                StfNode t = ln.Child("Type"); if (t != null) d.Type = (int)t.Num(0, 0);
                StfNode c = ln.Child("Conditions");
                if (c != null)
                {
                    StfNode h = c.Child("Headlight"); if (h != null) d.Headlight = (int)h.Num(0, 0);
                    StfNode u = c.Child("Unit"); if (u != null) d.Unit = (int)u.Num(0, 0);
                    StfNode p = c.Child("Penalty"); if (p != null) d.Penalty = (int)p.Num(0, 0);
                    StfNode cl = c.Child("Control"); if (cl != null) d.Control = (int)cl.Num(0, 0);
                    StfNode sv = c.Child("Service"); if (sv != null) d.Service = (int)sv.Num(0, 0);
                    StfNode td = c.Child("TimeOfDay"); if (td != null) d.TimeOfDay = (int)td.Num(0, 0);
                    StfNode we = c.Child("Weather"); if (we != null) d.Weather = (int)we.Num(0, 0);
                    StfNode cp = c.Child("Coupling"); if (cp != null) d.Coupling = (int)cp.Num(0, 0);
                }
                StfNode states = ln.Child("States");
                if (states != null)
                {
                    foreach (StfNode s in states.Children("State"))
                    {
                        LightState st = new LightState();
                        StfNode col = s.Child("LightColour");
                        if (col != null && col.Value(0) != null)
                        {
                            uint cv;
                            if (uint.TryParse(col.Value(0), System.Globalization.NumberStyles.HexNumber, null, out cv)) st.Colour = cv;
                        }
                        StfNode pos = s.Child("Position");
                        if (pos != null) { st.X = pos.Num(0, 0); st.Y = pos.Num(1, 0); st.Z = pos.Num(2, 0); }
                        StfNode r = s.Child("Radius"); if (r != null) st.Radius = r.Num(0, 0.5);
                        StfNode du = s.Child("Duration"); if (du != null) st.Duration = du.Num(0, 0);
                        d.States.Add(st);
                    }
                }
                Lights.Add(d);
            }
        }

        public string SmsPath(bool cab)
        {
            // Wagon-section sound = external; Engine-section sound = cab (internal)
            string want = cab ? "Engine" : "Wagon";
            foreach (SoundRef s in Sounds) if (s.Section == want && s.ResolvedPath != null) return s.ResolvedPath;
            foreach (SoundRef s in Sounds) if (s.ResolvedPath != null) return s.ResolvedPath;
            return null;
        }

        public string CabViewPath(int index)
        {
            if (index >= CabViews.Count) return null;
            string p = Path.Combine(Path.Combine(Folder, "CABVIEW"), CabViews[index]);
            return File.Exists(p) ? p : null;
        }

        public override string ToString() { return DisplayName; }
    }

    public static class LocoScanner
    {
        /// <summary>Finds and loads every .eng under TRAINS\TRAINSET in parallel.</summary>
        public static List<Loco> Scan(string trainsetDir, string orRoot, Action<int, int> progress)
        {
            List<string> engs = new List<string>();
            engs.AddRange(Directory.GetFiles(trainsetDir, "*.eng"));   // a single loco folder was picked
            foreach (string dir in Directory.GetDirectories(trainsetDir))
            {
                try
                {
                    foreach (string f in Directory.GetFiles(dir, "*.eng", SearchOption.AllDirectories))
                        if (f.IndexOf("\\LocoTester_Backup\\", StringComparison.OrdinalIgnoreCase) < 0) engs.Add(f);
                }
                catch { }
            }
            Loco[] result = new Loco[engs.Count];
            int done = 0;
            Parallel.For(0, engs.Count, delegate(int i)
            {
                result[i] = Loco.Load(engs[i], orRoot);
                int d = System.Threading.Interlocked.Increment(ref done);
                if (progress != null && (d % 8 == 0 || d == engs.Count)) progress(d, engs.Count);
            });
            List<Loco> list = new List<Loco>(result);
            list.Sort(delegate(Loco a, Loco b) { return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase); });
            return list;
        }

        /// <summary>Accepts the OR root, the TRAINS folder or TRAINSET itself.</summary>
        public static bool ResolveFolders(string picked, out string trainset, out string orRoot)
        {
            trainset = null; orRoot = null;
            if (string.IsNullOrEmpty(picked) || !Directory.Exists(picked)) return false;
            string p = picked.TrimEnd('\\');
            string name = Path.GetFileName(p).ToUpperInvariant();
            if (name == "TRAINSET") { trainset = p; orRoot = Path.GetDirectoryName(Path.GetDirectoryName(p)); return true; }
            if (name == "TRAINS" && Directory.Exists(Path.Combine(p, "TRAINSET"))) { trainset = Path.Combine(p, "TRAINSET"); orRoot = Path.GetDirectoryName(p); return true; }
            string t = Path.Combine(Path.Combine(p, "TRAINS"), "TRAINSET");
            if (Directory.Exists(t)) { trainset = t; orRoot = p; return true; }
            // a single loco folder or any folder with .eng files: find the OR root above it
            trainset = p; orRoot = null;
            for (string d = Path.GetDirectoryName(p); d != null; d = Path.GetDirectoryName(d))
                if (string.Equals(Path.GetFileName(d), "TRAINS", StringComparison.OrdinalIgnoreCase)) { orRoot = Path.GetDirectoryName(d); break; }
            return true;
        }
    }
}
