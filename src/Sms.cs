// Loco Tester - .sms sound file model.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LocoTester
{
    public enum TrigKind { Initial, Discrete, Variable, Random, Other }
    public enum CmdKind { None, StartLoop, StartLoopRelease, PlayOneShot, ReleaseLoopRelease, ReleaseLoopReleaseWithJump, EnableTrigger, DisableTrigger, SetStreamVolume }

    public class SmsTrigger
    {
        public TrigKind Kind;
        public int Discrete;            // trigger number for Discrete
        public string Variable;         // Speed, Variable1, Variable2, Variable3, Distance, BrakeCyl, CurveForce
        public bool Increasing;         // _Inc_Past vs _Dec_Past
        public double Threshold;
        public CmdKind Command;
        public List<string> Files = new List<string>();
        public bool RandomSelection;
        public double Value;            // SetStreamVolume value / Enable/Disable trigger index
        public string Raw;
    }

    public class SmsCurve
    {
        public string Control;          // SpeedControlled, Variable1Controlled ...
        public List<double> X = new List<double>();
        public List<double> Y = new List<double>();

        public double Eval(double x)
        {
            int n = X.Count;
            if (n == 0) return 1;
            if (x <= X[0]) return Y[0];
            if (x >= X[n - 1]) return Y[n - 1];
            for (int i = 1; i < n; i++)
            {
                if (x <= X[i])
                {
                    double t = (x - X[i - 1]) / Math.Max(1e-9, X[i] - X[i - 1]);
                    return Y[i - 1] + (Y[i] - Y[i - 1]) * t;
                }
            }
            return Y[n - 1];
        }
    }

    public class SmsStream
    {
        public int Index;
        public double Volume = 1;
        public int Priority;
        public List<SmsTrigger> Triggers = new List<SmsTrigger>();
        public List<SmsCurve> VolumeCurves = new List<SmsCurve>();
        public SmsCurve FrequencyCurve;
        public int DeclaredTriggerCount = -1;

        public IEnumerable<string> AllFiles()
        {
            foreach (SmsTrigger t in Triggers) foreach (string f in t.Files) yield return f;
        }

        /// <summary>Short human description: e.g. "Horn (trigger 8)", "Speed 0-38 m/s".</summary>
        public string Describe()
        {
            List<string> parts = new List<string>();
            foreach (SmsTrigger t in Triggers)
            {
                if (t.Kind == TrigKind.Discrete) { string n = TriggerNames.Name(t.Discrete); if (!parts.Contains(n)) parts.Add(n); }
                else if (t.Kind == TrigKind.Variable) { string n = t.Variable; if (!parts.Contains(n)) parts.Add(n); }
                else if (t.Kind == TrigKind.Initial) { if (!parts.Contains("Always")) parts.Add("Always"); }
            }
            return string.Join(", ", parts.ToArray());
        }
    }

    public class SmsGroup
    {
        public bool CabCam, ExternalCam, PassengerCam;
        public double Distance;
        public double Volume = 1;
        public int ScalabilityLevel;
        public List<SmsStream> Streams = new List<SmsStream>();
        public int DeclaredStreamCount = -1;
    }

    public class SmsFile
    {
        public string Path;
        public string Folder;
        public List<SmsGroup> Groups = new List<SmsGroup>();
        public bool HasHeader;
        public int BracketBalance;
        public string Error;

        public static SmsFile Load(string path)
        {
            SmsFile f = new SmsFile();
            f.Path = path;
            f.Folder = System.IO.Path.GetDirectoryName(path);
            try
            {
                Encoding enc;
                string text = Stf.ReadText(path, out enc);
                f.ParseText(text);
            }
            catch (Exception ex) { f.Error = ex.Message; }
            return f;
        }

        public int Rehomed;

        /// <summary>Which block may contain which (lower case). Used to rebuild damaged files.</summary>
        public static readonly Dictionary<string, string[]> Schema = MakeSchema();

        static Dictionary<string, string[]> MakeSchema()
        {
            Dictionary<string, string[]> s = new Dictionary<string, string[]>();
            s["scalabiltygroup"] = new[] { "tr_sms" };
            s["activation"] = new[] { "scalabiltygroup" };
            s["deactivation"] = new[] { "scalabiltygroup" };
            s["streams"] = new[] { "scalabiltygroup" };
            s["stream"] = new[] { "streams" };
            s["triggers"] = new[] { "stream" };
            s["priority"] = new[] { "stream" };
            s["volumecurve"] = new[] { "stream" };
            s["frequencycurve"] = new[] { "stream" };
            s["volume"] = new[] { "stream", "scalabiltygroup" };
            foreach (string t in new[] { "initial_trigger", "discrete_trigger", "variable_trigger", "random_trigger", "dist_travelled_trigger" })
                s[t] = new[] { "triggers" };
            foreach (string c in new[] { "startloop", "startlooprelease", "playoneshot", "releaselooprelease", "releaseloopreleasewithjump", "enabletrigger", "disabletrigger", "setstreamvolume" })
                s[c] = new[] { "initial_trigger", "discrete_trigger", "variable_trigger", "random_trigger", "dist_travelled_trigger" };
            s["file"] = new[] { "startloop", "startlooprelease", "playoneshot" };
            s["selectionmethod"] = new[] { "startloop", "startlooprelease", "playoneshot" };
            s["curvepoints"] = new[] { "volumecurve", "frequencycurve" };
            s["granularity"] = new[] { "volumecurve", "frequencycurve" };
            s["cabcam"] = new[] { "activation", "deactivation" };
            s["externalcam"] = new[] { "activation", "deactivation" };
            s["passengercam"] = new[] { "activation", "deactivation" };
            s["distance"] = new[] { "activation", "deactivation" };
            return s;
        }

        public void ParseText(string text)
        {
            HasHeader = text.TrimStart().StartsWith("SIMISA", StringComparison.Ordinal);
            int stray;
            BracketBalance = Stf.BracketBalance(text, out stray);
            StfNode root = Stf.Parse(text, Schema);
            Rehomed = Stf.Rehomed;
            StfNode sms = root.Child("Tr_SMS");
            if (sms == null) { Error = "Tr_SMS block not found"; return; }
            foreach (StfNode g in sms.Children("ScalabiltyGroup"))
            {
                SmsGroup grp = new SmsGroup();
                grp.ScalabilityLevel = (int)g.Num(0, 0);
                StfNode act = g.Child("Activation");
                if (act != null)
                {
                    grp.CabCam = act.Child("CabCam") != null;
                    grp.ExternalCam = act.Child("ExternalCam") != null;
                    grp.PassengerCam = act.Child("PassengerCam") != null;
                    StfNode d = act.Child("Distance"); if (d != null) grp.Distance = d.Num(0, 0);
                }
                StfNode v = g.Child("Volume"); if (v != null) grp.Volume = v.Num(0, 1);
                StfNode streams = g.Child("Streams");
                if (streams != null)
                {
                    grp.DeclaredStreamCount = (int)streams.Num(0, -1);
                    foreach (StfNode s in streams.Children("Stream")) grp.Streams.Add(ReadStream(s, grp.Streams.Count));
                }
                Groups.Add(grp);
            }
        }

        static SmsStream ReadStream(StfNode s, int index)
        {
            SmsStream st = new SmsStream();
            st.Index = index;
            StfNode v = s.Child("Volume"); if (v != null) st.Volume = v.Num(0, 1);
            StfNode p = s.Child("Priority"); if (p != null) st.Priority = (int)p.Num(0, 0);
            StfNode tr = s.Child("Triggers");
            if (tr != null)
            {
                st.DeclaredTriggerCount = (int)tr.Num(0, -1);
                foreach (StfNode t in tr.Children()) st.Triggers.Add(ReadTrigger(t));
            }
            foreach (StfNode vc in s.Children("VolumeCurve")) st.VolumeCurves.Add(ReadCurve(vc));
            StfNode fc = s.Child("FrequencyCurve");
            if (fc != null) st.FrequencyCurve = ReadCurve(fc);
            return st;
        }

        static SmsCurve ReadCurve(StfNode c)
        {
            SmsCurve cv = new SmsCurve();
            cv.Control = c.Value(0) ?? "SpeedControlled";
            StfNode pts = c.Child("CurvePoints");
            if (pts != null)
            {
                List<string> vals = pts.Values();
                for (int i = 1; i + 1 < vals.Count; i += 2)
                {
                    cv.X.Add(Stf.ParseNumber(vals[i], 0));
                    cv.Y.Add(Stf.ParseNumber(vals[i + 1], 0));
                }
            }
            return cv;
        }

        static SmsTrigger ReadTrigger(StfNode t)
        {
            SmsTrigger r = new SmsTrigger();
            r.Raw = t.Name;
            string name = (t.Name ?? "").ToLowerInvariant();
            if (name == "initial_trigger") r.Kind = TrigKind.Initial;
            else if (name == "discrete_trigger") { r.Kind = TrigKind.Discrete; r.Discrete = (int)t.Num(0, -1); }
            else if (name == "variable_trigger")
            {
                r.Kind = TrigKind.Variable;
                string w = t.Value(0) ?? "";
                int us = w.IndexOf('_');
                r.Variable = us > 0 ? w.Substring(0, us) : w;
                r.Increasing = w.IndexOf("_Inc_", StringComparison.OrdinalIgnoreCase) >= 0;
                r.Threshold = t.Num(1, 0);
            }
            else if (name == "random_trigger") r.Kind = TrigKind.Random;
            else r.Kind = TrigKind.Other;

            foreach (StfNode c in t.Children())
            {
                string cn = (c.Name ?? "").ToLowerInvariant();
                CmdKind k = CmdKind.None;
                if (cn == "startloop") k = CmdKind.StartLoop;
                else if (cn == "startlooprelease") k = CmdKind.StartLoopRelease;
                else if (cn == "playoneshot") k = CmdKind.PlayOneShot;
                else if (cn == "releaselooprelease") k = CmdKind.ReleaseLoopRelease;
                else if (cn == "releaseloopreleasewithjump") k = CmdKind.ReleaseLoopReleaseWithJump;
                else if (cn == "enabletrigger") k = CmdKind.EnableTrigger;
                else if (cn == "disabletrigger") k = CmdKind.DisableTrigger;
                else if (cn == "setstreamvolume") k = CmdKind.SetStreamVolume;
                if (k == CmdKind.None) continue;
                r.Command = k;
                if (k == CmdKind.SetStreamVolume || k == CmdKind.EnableTrigger || k == CmdKind.DisableTrigger) r.Value = c.Num(0, 0);
                foreach (StfNode f in c.Children("File")) if (f.Value(0) != null) r.Files.Add(f.Value(0));
                StfNode sel = c.Child("SelectionMethod");
                if (sel != null && (sel.Value(0) ?? "").ToLowerInvariant().Contains("random")) r.RandomSelection = true;
            }
            return r;
        }

        /// <summary>The group used for the outside (or cab) view.</summary>
        public SmsGroup BestGroup(bool cab)
        {
            SmsGroup best = null;
            foreach (SmsGroup g in Groups)
            {
                bool ok = cab ? g.CabCam : (g.ExternalCam || (!g.CabCam && !g.PassengerCam));
                if (ok && (best == null || g.ScalabilityLevel > best.ScalabilityLevel)) best = g;
            }
            if (best == null && Groups.Count > 0) best = Groups[0];
            return best;
        }

        /// <summary>Open Rails looks for .wav next to the .sms, then in the global SOUND folder.</summary>
        public string ResolveWav(string file, string orRoot)
        {
            if (string.IsNullOrEmpty(file) || file.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0) return null;
            string a = System.IO.Path.Combine(Folder, file);
            if (File.Exists(a)) return a;
            if (!string.IsNullOrEmpty(orRoot))
            {
                string b = System.IO.Path.Combine(System.IO.Path.Combine(orRoot, "SOUND"), file);
                if (File.Exists(b)) return b;
            }
            return null;
        }
    }

    /// <summary>Names of the MSTS / Open Rails discrete sound triggers.
    /// Mapping checked against which .wav files the installed locos attach to each number.</summary>
    public static class TriggerNames
    {
        public const int SanderOn = 4, SanderOff = 5, WiperOn = 6, WiperOff = 7, HornOn = 8, HornOff = 9,
            BellOn = 10, BellOff = 11, CompressorOn = 12, CompressorOff = 13, TrainBrakePressureIncrease = 14,
            ReverserChange = 15, ThrottleChange = 16, TrainBrakeChange = 17, EngineBrakeChange = 18,
            DynamicBrakeChange = 20, EngineBrakePressureIncrease = 21, EngineBrakePressureDecrease = 22,
            EnginePowerOn = 23, EnginePowerOff = 24, LightSwitchToggle = 37,
            Pantograph1Up = 45, Pantograph1Down = 46, PantographToggle = 47, TrainBrakePressureDecrease = 54,
            VigilanceAlarmOn = 56, Pantograph2Up = 66, Pantograph2Down = 67,
            DoorOpen = 105, DoorClose = 106, CircuitBreakerOpen = 150, CircuitBreakerClosing = 151, CircuitBreakerClosed = 152,
            CabRadioOn = 162;

        public static string Name(int n)
        {
            switch (n)
            {
                case 2: return "Dynamic brake on";
                case 3: return "Dynamic brake off";
                case 4: return "Sander on";
                case 5: return "Sander off";
                case 6: return "Wiper on";
                case 7: return "Wiper off";
                case 8: return "Horn on";
                case 9: return "Horn off";
                case 10: return "Bell on";
                case 11: return "Bell off";
                case 12: return "Compressor on";
                case 13: return "Compressor off";
                case 14: return "Train brake pressure up";
                case 15: return "Reverser change";
                case 16: return "Throttle change";
                case 17: return "Train brake handle";
                case 18: return "Engine brake handle";
                case 20: return "Dynamic brake handle";
                case 21: return "Engine brake pressure up";
                case 22: return "Engine brake pressure down";
                case 23: return "Power on";
                case 24: return "Power off";
                case 25: return "Vigilance";
                case 26: return "Sander valve";
                case 37: return "Light switch";
                case 45: return "Pantograph 1 up";
                case 46: return "Pantograph 1 down";
                case 47: return "Pantograph toggle";
                case 48: return "Cab control";
                case 51: return "Emergency brake";
                case 53: return "Brake application";
                case 54: return "Brake release";
                case 56: return "Alerter / deadman";
                case 58: case 59: case 60: return "Couple";
                case 61: case 62: case 63: return "Uncouple";
                case 66: return "Pantograph 2 up";
                case 67: return "Pantograph 2 down";
                case 105: return "Doors open";
                case 106: return "Doors close";
                case 150: return "Circuit breaker open";
                case 151: return "Circuit breaker closing";
                case 152: return "Circuit breaker closed";
                case 162: return "Cab radio";
                default: return "Trigger " + n;
            }
        }
    }
}
