// Loco Tester - entry point.
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Loco Tester")]
[assembly: AssemblyDescription("Locomotive tester for Open Rails and MSTS: 3D preview, sound, lights, pantograph and auto-repair")]
[assembly: AssemblyCompany("Piscongentine")]
[assembly: AssemblyProduct("Loco Tester")]
[assembly: AssemblyCopyright("© 2026 Piscongentine")]
[assembly: AssemblyVersion("1.1.1.0")]
[assembly: AssemblyFileVersion("1.1.1.0")]
[assembly: AssemblyInformationalVersion("1.1.1 \"Power Up\"")]
[assembly: ComVisible(false)]

namespace LocoTester
{
    public static class AppInfo
    {
        public const string Name = "Loco Tester";
        public const string Version = "1.1.1";
        public const string VersionName = "Power Up";
        public const string Author = "Piscongentine";
    }

    public static class Program
    {
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length >= 1 && args[0] == "--checkupdate")
            {
                // test hook: --checkupdate [pretend-version]; prints the result, never opens a browser
                AttachConsole(-1);
                Updater.Result u = Updater.Check(args.Length > 1 ? args[1] : AppInfo.Version);
                Console.WriteLine();
                Console.WriteLine(u.Error ?? (u.Current + " -> GitHub " + u.Latest + (u.Newer ? " : NEWER, would open " + u.Url : " : up to date, browser stays closed")));
                return u.Error == null ? 0 : 1;
            }
            if (args.Length >= 1 && args[0] == "--selftest")
            {
                AttachConsole(-1);
                string trainset = args.Length > 1 ? args[1] : @"C:\OR\TRAINS\TRAINSET";
                string source = args.Length > 2 ? args[2] : "HWH FORTUNE WAP5 30112";
                string report = args.Length > 3 ? args[3] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-report.txt");
                return SelfTest.Run(trainset, source, report);
            }
            if (args.Length >= 2 && args[0] == "--soundtest")
            {
                AttachConsole(-1);
                return SoundTest(args[1], args.Length > 2 ? args[2] : @"C:\OR");
            }
            if (args.Length >= 1 && args[0] == "--cablights")
            {
                // --cablights [folder]: which locos have a working cab light (CABLIGHT images) in their cab views
                AttachConsole(-1);
                string ts, root;
                if (!LocoScanner.ResolveFolders(args.Length > 1 ? args[1] : @"C:\OR", out ts, out root)) { Console.WriteLine("folder not found"); return 1; }
                int yes = 0, no = 0, none = 0;
                foreach (Loco l in LocoScanner.Scan(ts, root, null))
                {
                    string state = "no cab view";
                    for (int i = 0; i < Math.Max(1, l.CabViews.Count); i++)
                    {
                        string p = l.CabViewPath(i);
                        if (p == null) continue;
                        CabFile c = CabFile.Load(p);
                        int lit = 0; for (int v = 0; v < c.Views.Count; v++) if (c.HasCabLight(v)) lit++;
                        state = lit > 0 ? "CAB LIGHT (" + lit + "/" + c.Views.Count + " views lit)" : "no cab light";
                        if (lit > 0) break;
                    }
                    if (state.StartsWith("CAB")) yes++; else if (state == "no cab light") no++; else none++;
                    Console.WriteLine(string.Format("{0,-44} {1}", l.DisplayName.Length > 44 ? l.DisplayName.Substring(0, 44) : l.DisplayName, state));
                }
                Console.WriteLine("with cab light: " + yes + " · without: " + no + " · no cab view: " + none);
                return 0;
            }
            if (args.Length >= 4 && args[0] == "--swap")
            {
                // --swap <eng> horn|bell|sander <file.wav>|restore [orRoot]  (same as the Replace sounds window)
                AttachConsole(-1);
                string orRoot = args.Length > 4 ? args[4] : @"C:\OR";
                Loco l = Loco.Load(args[1], orRoot);
                foreach (SwapSlot s in SoundSwap.Slots(l))
                    if (s.Id == args[2])
                    {
                        try { Console.WriteLine(args[3] == "restore" ? SoundSwap.Restore(l, s) : SoundSwap.Replace(l, s, args[3], true)); return 0; }
                        catch (Exception ex) { Console.WriteLine("failed: " + ex.Message); return 1; }
                    }
                Console.WriteLine("this loco has no " + args[2] + " sound");
                return 1;
            }
            return Gui.Run(args);
        }

        /// <summary>Plays a loco's sounds through the real mixer and reports what produced signal.</summary>
        static int SoundTest(string eng, string orRoot)
        {
            Loco l = Loco.Load(eng, orRoot);
            using (AudioEngine a = new AudioEngine())
            {
                if (a.InitError != null) { Console.WriteLine("audio: " + a.InitError); return 2; }
                a.MasterVolume = 0.05f;   // quiet: this is a check, not a demo
                SmsFile f = SmsFile.Load(l.SmsPath(false));
                SoundSim s = new SoundSim(a, f, false, orRoot);
                SimState st = new SimState();
                s.Start(st);
                Func<string, int, double, float> step = delegate(string what, int trig, double secs)
                {
                    if (trig > 0) s.Fire(trig);
                    float peak = 0;
                    DateTime end = DateTime.Now.AddSeconds(secs);
                    while (DateTime.Now < end) { s.Update(st); System.Threading.Thread.Sleep(30); peak = Math.Max(peak, a.PeakLevel); }
                    Console.WriteLine(string.Format("{0,-26} voices {1,2}  peak {2:0.000}", what, a.ActiveVoices, peak));
                    return peak;
                };
                int fails = 0;
                if (step("idle (initial triggers)", 0, 1.0) <= 0) Console.WriteLine("  (no idle sound)");
                if (step("horn on", TriggerNames.HornOn, 1.2) <= 0.0005) fails++;
                step("horn off", TriggerNames.HornOff, 0.6);
                step("pantograph up", TriggerNames.Pantograph1Up, 1.0);
                step("brake apply", TriggerNames.TrainBrakePressureIncrease, 0.8);
                Console.WriteLine("variable scale: V1 x" + s.VarScale(1) + "  V2 x" + s.VarScale(2) + "  V3 x" + s.VarScale(3));
                for (int k = 0; k <= 10; k++) { st.Speed = k * 2.8; st.Variable1 = st.Variable2 = Math.Min(1, k * 0.1); step("speed " + Math.Round(st.Speed * 3.6) + " km/h, throttle " + k * 10 + "%", 0, 0.25); }
                foreach (string p in s.Problems) Console.WriteLine("problem: " + p);
                Console.WriteLine(fails == 0 ? "SOUND TEST PASSED" : "SOUND TEST FAILED");
                return fails;
            }
        }
    }
}
