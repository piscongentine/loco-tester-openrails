// Loco Tester - checks this PC for what Loco Tester needs. Reads only; never downloads or changes anything.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace LocoTester
{
    public static class SysCheck
    {
        public class Result
        {
            public string Name, Detail, Advice;
            public int Level;       // 0 ok, 1 warning, 2 blocking, 3 info
        }

        public static bool Blocking;
        public static readonly List<string> OpenRailsFolders = new List<string>();

        [DllImport("winmm.dll")] static extern int waveOutGetNumDevs();

        [StructLayout(LayoutKind.Sequential)]
        class MEMORYSTATUSEX { public uint dwLength = 64, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }
        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);

        static Result R(string n, int lvl, string d, string a) { return new Result { Name = n, Level = lvl, Detail = d, Advice = a }; }

        public static IEnumerable<Result> Run()
        {
            Blocking = false;
            // 1. Windows
            // the real version comes from the registry (Environment.OSVersion reports 6.2 to unmanifested programs)
            int major = 6, build = 0; string display = "";
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                if (k != null)
                {
                    if (k.GetValue("CurrentMajorVersionNumber") is int) major = (int)k.GetValue("CurrentMajorVersionNumber");
                    int.TryParse(k.GetValue("CurrentBuildNumber") as string, out build);
                    display = k.GetValue("DisplayVersion") as string ?? "";
                }
            string os = major >= 10 ? (build >= 22000 ? "Windows 11" : "Windows 10") : "Windows " + Environment.OSVersion.Version.Major + "." + Environment.OSVersion.Version.Minor;
            yield return major >= 10
                ? R("Windows", 0, os + (display.Length > 0 ? " " + display : "") + " (build " + build + ", " + (Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit") + ")", null)
                : R("Windows", 1, os, "Loco Tester is made for Windows 10 and 11. Older Windows may work with .NET Framework 4.8 installed.");
            // 2. .NET Framework 4.8 (needed to run Loco Tester and this setup)
            int release = 0;
            using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                if (k != null && k.GetValue("Release") is int) release = (int)k.GetValue("Release");
            if (release >= 528040) yield return R(".NET Framework", 0, "4.8 or newer is installed", null);
            else if (release >= 461808) yield return R(".NET Framework", 1, "4.7.2 found; 4.8 recommended", "Get .NET Framework 4.8 from Microsoft: dotnet.microsoft.com/download/dotnet-framework/net48");
            else { Blocking = true; yield return R(".NET Framework", 2, "4.8 is missing", "Install .NET Framework 4.8 from Microsoft: dotnet.microsoft.com/download/dotnet-framework/net48"); }

            // 3. Graphics: hardware 3D (WPF render tier) and the card / driver
            int tier = RenderCapability.Tier >> 16;
            string gpu = "", driver = "";
            // graphics card name and driver from the registry (display adapter class)
            try
            {
                using (RegistryKey cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                    if (cls != null)
                        foreach (string sub in cls.GetSubKeyNames())
                        {
                            if (sub.Length != 4) continue;
                            using (RegistryKey k = cls.OpenSubKey(sub))
                            {
                                if (k == null) continue;
                                string n = k.GetValue("DriverDesc") as string;
                                if (string.IsNullOrEmpty(n) || n.Contains("Basic Display") || n.Contains("Remote")) continue;
                                if (gpu.Length > 0) gpu += " + ";
                                gpu += n;
                                if (driver.Length == 0) driver = (k.GetValue("DriverVersion") as string ?? "") + (k.GetValue("DriverDate") is string ? " (" + k.GetValue("DriverDate") + ")" : "");
                            }
                        }
            }
            catch { }
            string tierText = tier == 2 ? "full hardware 3D" : tier == 1 ? "partial hardware 3D" : "software only (slow)";
            yield return R("Graphics", tier == 2 ? 0 : 1, (gpu.Length > 0 ? gpu : "graphics card") + " · " + tierText + (driver.Length > 0 ? " · driver " + driver : ""),
                tier == 2 ? "HD 3D preview with full-resolution textures will run smoothly." : "Update your graphics driver from the card maker (NVIDIA / AMD / Intel) for smooth HD 3D. The HD button in the 3D view can be turned off on slow PCs.");

            // 4. Sound output
            int devs = 0; try { devs = waveOutGetNumDevs(); } catch { }
            yield return devs > 0 ? R("Sound", 0, devs + " output device(s)", null) : R("Sound", 1, "no sound output found", "Connect speakers or headphones to hear the loco sounds. Everything else works without sound.");

            // 5. Memory and screen
            MEMORYSTATUSEX m = new MEMORYSTATUSEX();
            double gb = GlobalMemoryStatusEx(m) ? m.ullTotalPhys / 1073741824.0 : 0;
            yield return gb >= 4 ? R("Memory", 0, gb.ToString("0.#") + " GB", null) : R("Memory", 1, gb.ToString("0.#") + " GB", "4 GB or more is recommended for HD textures.");
            double sw = SystemParameters.PrimaryScreenWidth, sh = SystemParameters.PrimaryScreenHeight;
            yield return sw >= 1280 && sh >= 720 ? R("Screen", 0, sw + " × " + sh, null) : R("Screen", 1, sw + " × " + sh, "A screen of 1280 × 720 or larger is recommended.");

            // 6. Open Rails / MSTS content on this PC
            OpenRailsFolders.Clear();
            foreach (string f in FindOpenRails()) if (!OpenRailsFolders.Contains(f)) OpenRailsFolders.Add(f);
            if (OpenRailsFolders.Count > 0)
            {
                yield return R("Open Rails / MSTS", 0, string.Join(",  ", OpenRailsFolders.ToArray()), "Loco Tester will open " + OpenRailsFolders[0] + " the first time it starts.");
                // 7. DB Tracks (used for the real track under the loco)
                bool db = false;
                foreach (string f in OpenRailsFolders) if (File.Exists(Path.Combine(f, @"GLOBAL\SHAPES\A1t250mStrt.s")) || File.Exists(Path.Combine(f, @"GLOBAL\SHAPES\A1t100mStrt.s"))) db = true;
                yield return db ? R("DB Tracks", 0, "found: the 3D view uses the real DB track", null)
                                : R("DB Tracks", 3, "not found (optional)", "Without DB Tracks the 3D view draws a simple track instead.");
            }
            else yield return R("Open Rails / MSTS", 1, "no TRAINS\\TRAINSET folder found", "You can pick your Open Rails or MSTS folder with Browse… when Loco Tester opens.");
        }

        /// <summary>Common install places, the MSTS registry entry, and a shallow scan of every fixed drive.</summary>
        static IEnumerable<string> FindOpenRails()
        {
            List<string> candidates = new List<string>();
            foreach (string k in new[] { @"SOFTWARE\Microsoft\Microsoft Games\Train Simulator\1.0", @"SOFTWARE\WOW6432Node\Microsoft\Microsoft Games\Train Simulator\1.0" })
                using (RegistryKey r = Registry.LocalMachine.OpenSubKey(k)) if (r != null && r.GetValue("Path") is string) candidates.Add((string)r.GetValue("Path"));
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                string root = d.RootDirectory.FullName;
                candidates.Add(Path.Combine(root, "OR"));
                candidates.Add(Path.Combine(root, "OpenRails"));
                candidates.Add(Path.Combine(root, "Open Rails"));
                candidates.Add(Path.Combine(root, "MSTS"));
                candidates.Add(Path.Combine(root, @"Program Files (x86)\Microsoft Games\Train Simulator"));
                candidates.Add(Path.Combine(root, @"Program Files\Microsoft Games\Train Simulator"));
                candidates.Add(Path.Combine(root, @"Games\OR"));
                candidates.Add(Path.Combine(root, @"Games\Open Rails"));
                // shallow scan: X:\*\TRAINS\TRAINSET and X:\*\*\TRAINS\TRAINSET
                try
                {
                    foreach (string a in Directory.GetDirectories(root))
                    {
                        if (Skip(a)) continue;
                        candidates.Add(a);
                        try { foreach (string b in Directory.GetDirectories(a)) if (!Skip(b)) candidates.Add(b); } catch { }
                    }
                }
                catch { }
            }
            foreach (string c in candidates)
            {
                bool ok = false;
                try { ok = Directory.Exists(Path.Combine(c, @"TRAINS\TRAINSET")); } catch { }
                if (ok) yield return c.TrimEnd('\\');
            }
        }

        static bool Skip(string dir)
        {
            string n = Path.GetFileName(dir).ToLowerInvariant();
            return n == "windows" || n == "$recycle.bin" || n == "system volume information" || n == "programdata" || n == "recovery" || n.StartsWith("$");
        }

        /// <summary>Lets Loco Tester open the found Open Rails folder on its first start (only if it has no setting yet).</summary>
        public static void RememberFolder()
        {
            if (OpenRailsFolders.Count == 0) return;
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LocoTester");
                string f = Path.Combine(dir, "settings.txt");
                if (File.Exists(f)) return;
                Directory.CreateDirectory(dir);
                File.WriteAllText(f, "folder=" + OpenRailsFolders[0] + Environment.NewLine);
            }
            catch { }
        }
    }
}
