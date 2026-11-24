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
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0 \"First Light\"")]
[assembly: ComVisible(false)]

namespace LocoTester
{
    public static class AppInfo
    {
        public const string Name = "Loco Tester";
        public const string Version = "1.0";
        public const string VersionName = "First Light";
        public const string Author = "Piscongentine";
    }

    public static class Program
    {
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        [STAThread]
        public static int Main(string[] args)
        {
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
