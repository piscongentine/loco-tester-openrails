// Loco Tester - self test: makes damaged copies of a real loco (LT_DUMMY_*), runs the
// auto-repair on them and checks the result. Also checks that every loco's shape,
// textures and sounds can be read. Run:  "Loco Tester.exe" --selftest <TRAINSET folder> <source loco folder name>
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LocoTester
{
    public static class SelfTest
    {
        public static int Run(string trainset, string sourceName, string reportPath)
        {
            StringBuilder rep = new StringBuilder();
            Action<string> log = delegate(string s) { rep.AppendLine(s); Console.WriteLine(s); };
            string orRoot = Path.GetDirectoryName(Path.GetDirectoryName(trainset));
            int failures = 0;
            log("Loco Tester self-test  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            log("TRAINSET: " + trainset);
            log("");

            foreach (string old in Directory.GetDirectories(trainset, "LT_DUMMY_*")) Directory.Delete(old, true);
            Stopwatch sw = Stopwatch.StartNew();
            List<Loco> all = LocoScanner.Scan(trainset, orRoot, null);
            log("Scan: " + all.Count + " locomotives in " + sw.ElapsedMilliseconds + " ms");

            // ---------- pick a healthy source loco (no sound errors, one .eng in its folder, smallest)
            sw.Restart();
            Loco pick = null; long pickSize = long.MaxValue; int healthy = 0;
            foreach (Loco l in all)
            {
                if (Doctor.ErrorCount(l, orRoot) != 0 || l.SmsPath(false) == null || l.SmsPath(true) == null) continue;
                healthy++;
                long size = 0;
                foreach (string f in LocoFiles(l)) size += new FileInfo(f).Length;
                bool electric = string.Equals(l.EngineType, "Electric", StringComparison.OrdinalIgnoreCase);
                long rank = electric ? size : size * 2;
                if (string.Equals(l.FolderName, sourceName, StringComparison.OrdinalIgnoreCase)) rank = -1;
                if (rank < pickSize) { pickSize = rank; pick = l; }
            }
            log("Health scan: " + healthy + " of " + all.Count + " locos have no sound errors (" + sw.ElapsedMilliseconds + " ms)");
            if (pick == null) { log("No healthy loco to copy; cannot run the repair test."); File.WriteAllText(reportPath, rep.ToString()); return 1; }
            sourceName = pick.FolderName;
            log("Source for dummies: " + pick.DisplayName + " (" + sourceName + ", " + pick.EngineType + ")");

            // ---------- make dummies (copy only this loco's own files)
            string[] names = { "LT_DUMMY_1_MissingSms", "LT_DUMMY_2_Truncated", "LT_DUMMY_3_Garbage", "LT_DUMMY_4_NoSound" };
            List<Loco> dummies = new List<Loco>();
            List<string> srcFiles = LocoFiles(pick);
            for (int i = 0; i < names.Length; i++)
            {
                string dst = Path.Combine(trainset, names[i]);
                if (Directory.Exists(dst)) Directory.Delete(dst, true);
                foreach (string f in srcFiles)
                {
                    string rel = f.Substring(pick.Folder.Length).TrimStart('\\');
                    string to = Path.Combine(dst, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.Copy(f, to, true);
                }
                string eng = Path.Combine(dst, Path.GetFileName(pick.EngPath));
                string newEng = Path.Combine(dst, names[i] + ".eng");
                File.Move(eng, newEng);
                Encoding enc;
                string t = Stf.ReadText(newEng, out enc);
                // rename so Open Rails sees a different loco: Wagon ( name ) and Engine ( name ) and Name ( "..." )
                t = Regex.Replace(t, @"(?im)^(\s*Wagon\s*\(\s*)[^\r\n]*", "${1}" + names[i]);
                t = Regex.Replace(t, @"(?im)^(\s*Engine\s*\(\s*)[^\r\n]*", "${1}" + names[i]);
                if (Regex.IsMatch(t, @"(?i)\bName\s*\(")) t = Regex.Replace(t, @"(?i)(\bName\s*\(\s*)""?[^)""]*""?", "${1}\"Loco Tester dummy " + (i + 1) + "\"", RegexOptions.None, TimeSpan.FromSeconds(2));
                Stf.WriteText(newEng, t, enc);
                dummies.Add(Loco.Load(newEng, orRoot));
            }
            log("Created " + dummies.Count + " dummy locos copied from " + sourceName);

            // baseline: the clean copy must have no errors
            int baseErr = 0;
            foreach (Issue i in Doctor.Diagnose(dummies[0], orRoot)) if (i.Severity == Severity.Error) { baseErr++; log("  baseline problem: " + i.Message); }
            log("Baseline (undamaged copy) errors: " + baseErr);
            log("");

            // ---------- damage
            Loco d1 = dummies[0], d2 = dummies[1], d3 = dummies[2], d4 = dummies[3];
            string ext1 = d1.SmsPath(false);
            File.Delete(ext1);
            log("DAMAGE 1: deleted outside sound file " + Path.GetFileName(ext1));

            string int2 = d2.SmsPath(true), ext2 = d2.SmsPath(false);
            Encoding e2; string t2 = Stf.ReadText(int2, out e2);
            Stf.WriteText(int2, t2.Substring(0, (int)(t2.Length * 0.55)), e2);
            string t2b = Stf.ReadText(ext2, out e2);
            t2b = Regex.Replace(t2b, @"Streams\s*\(\s*\d+", "Streams ( 99", RegexOptions.None);
            int vol = t2b.IndexOf("Volume", StringComparison.Ordinal);
            t2b = t2b.Insert(vol, ") ");
            Stf.WriteText(ext2, t2b, e2);
            log("DAMAGE 2: cut cab sound file at 55%; outside file: wrong Streams count + stray ')'");

            string ext3 = d3.SmsPath(false);
            Encoding e3; string t3 = Stf.ReadText(ext3, out e3);
            t3 = t3.Substring(t3.IndexOf('\n') + 1);  // drop header
            Random rnd = new Random(7);
            StringBuilder g = new StringBuilder(t3);
            for (int k = 0; k < 25; k++) g.Insert(rnd.Next(g.Length / 2, g.Length - 10), (char)rnd.Next(1, 8));
            t3 = g.ToString();
            SmsFile sms3 = SmsFile.Load(ext3);
            List<string> wavs = new List<string>();
            foreach (SmsGroup gg in sms3.Groups) foreach (SmsStream s in gg.Streams) foreach (string f in s.AllFiles()) if (!wavs.Contains(f)) wavs.Add(f);
            string typoWav = wavs.Count > 0 ? wavs[0] : null, deletedWav = wavs.Count > 1 ? wavs[wavs.Count - 1] : null;
            if (typoWav != null)
            {
                string typo = typoWav.Length > 6 ? typoWav.Substring(0, typoWav.Length - 6) + "X" + typoWav.Substring(typoWav.Length - 5) : "x" + typoWav;
                t3 = t3.Replace("\"" + typoWav + "\"", "\"" + typo + "\"");
                log("DAMAGE 3: removed header, inserted 25 control characters, renamed reference " + typoWav + " -> " + typo);
            }
            Stf.WriteText(ext3, t3, e3);
            if (deletedWav != null)
            {
                string wp = Path.Combine(Path.GetDirectoryName(ext3), deletedWav);
                if (File.Exists(wp)) { File.Delete(wp); log("          deleted sound file " + deletedWav); }
            }

            string eng4 = d4.EngPath; Encoding e4; string t4 = Stf.ReadText(eng4, out e4);
            t4 = Regex.Replace(t4, @"(?im)^\s*Sound\s*\([^\r\n]*\r?\n", "");
            Stf.WriteText(eng4, t4, e4);
            string int4 = d4.SmsPath(true);
            File.WriteAllBytes(int4, new byte[0]);
            log("DAMAGE 4: removed every Sound ( ) line from the .eng; emptied " + Path.GetFileName(int4));
            log("");

            // ---------- repair + verify
            List<Loco> donors = new List<Loco>();
            foreach (Loco l in all) if (!l.FolderName.StartsWith("LT_DUMMY", StringComparison.OrdinalIgnoreCase)) donors.Add(l);
            for (int i = 0; i < dummies.Count; i++)
            {
                Loco dl = Loco.Load(dummies[i].EngPath, orRoot);
                List<Issue> before = Doctor.Diagnose(dl, orRoot);
                int errB = 0; foreach (Issue x in before) if (x.Severity == Severity.Error) errB++;
                log("== " + names[i] + ": detected " + before.Count + " problem(s), " + errB + " error(s)");
                foreach (Issue x in before) log("   found: " + x);
                if (errB == 0) { log("   FAIL: damage was not detected"); failures++; }
                sw.Restart();
                RepairResult rr = Doctor.Repair(dl, orRoot, donors);
                foreach (string a in rr.Actions) log("   fix:   " + a);
                foreach (Issue x in rr.After) log("   left:  " + x);
                // play test: every stream of every sms must load its wav
                Loco fixedLoco = Loco.Load(dummies[i].EngPath, orRoot);
                int playable = 0, unplayable = 0;
                foreach (SoundRef s in fixedLoco.Sounds)
                {
                    if (s.ResolvedPath == null) { unplayable++; continue; }
                    SmsFile sf = SmsFile.Load(s.ResolvedPath);
                    foreach (SmsGroup gg in sf.Groups) foreach (SmsStream st in gg.Streams) foreach (string f in st.AllFiles())
                    {
                        string wp = sf.ResolveWav(f, orRoot);
                        if (wp != null && WavData.Get(wp).Error == null) playable++; else unplayable++;
                    }
                }
                bool horn = false;
                foreach (SoundRef s in fixedLoco.Sounds) if (s.ResolvedPath != null) foreach (SmsGroup gg in SmsFile.Load(s.ResolvedPath).Groups) foreach (SmsStream st in gg.Streams) foreach (SmsTrigger tr in st.Triggers) if (tr.Kind == TrigKind.Discrete && tr.Discrete == 8) horn = true;
                bool pass = rr.ErrorsAfter == 0 && unplayable == 0 && fixedLoco.Sounds.Count >= 2 && horn;
                log("   result: errors " + rr.ErrorsBefore + " -> " + rr.ErrorsAfter + ", sound refs playable " + playable + ", unplayable " + unplayable + ", horn present " + horn + ", took " + sw.ElapsedMilliseconds + " ms  => " + (pass ? "PASS" : "FAIL"));
                if (!pass) failures++;
                log("");
            }

            // ---------- read test over every loco
            int shapesOk = 0, shapesFail = 0, texOk = 0, texFail = 0, smsOk = 0, smsFail = 0;
            sw.Restart();
            foreach (Loco l in all)
            {
                if (l.ShapePath != null)
                {
                    ShapeModel m = ShapeReader.Load(l.ShapePath);
                    if (m.Error == null && m.Meshes.Count > 0) shapesOk++; else { shapesFail++; log("   shape problem: " + l.DisplayName + ": " + (m.Error ?? "no meshes")); }
                    HashSet<string> texs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (ShapeMesh me in m.Meshes) if (me.Texture != null) texs.Add(me.Texture);
                    foreach (string tx in texs)
                    {
                        string tp = Path.Combine(l.Folder, tx);
                        if (!File.Exists(tp)) continue;
                        if (AceReader.Load(tp, 256) != null) texOk++; else { texFail++; if (texFail < 15) log("   texture problem: " + tp); }
                    }
                }
                foreach (SoundRef s in l.Sounds) if (s.ResolvedPath != null) { SmsFile sf = SmsFile.Load(s.ResolvedPath); if (sf.Error == null && sf.Groups.Count > 0) smsOk++; else smsFail++; }
            }
            log("Read test over all locos (" + sw.ElapsedMilliseconds + " ms): shapes " + shapesOk + " ok / " + shapesFail + " failed; textures " + texOk + " ok / " + texFail + " failed; sound files " + smsOk + " ok / " + smsFail + " failed");
            log("");
            log(failures == 0 ? "SELF-TEST PASSED" : "SELF-TEST FAILED: " + failures + " check(s)");
            File.WriteAllText(reportPath, rep.ToString(), Encoding.UTF8);
            return failures;
        }

        /// <summary>The files one loco needs: its .eng, shape (+.sd), the textures the shape uses,
        /// the sound files it plays and its cab view folder.</summary>
        public static List<string> LocoFiles(Loco l)
        {
            HashSet<string> r = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            r.Add(l.EngPath);
            if (l.ShapePath != null)
            {
                r.Add(l.ShapePath);
                string sd = l.ShapePath + "d";
                if (File.Exists(sd)) r.Add(sd);
                ShapeModel m = ShapeReader.Load(l.ShapePath);
                foreach (ShapeMesh me in m.Meshes)
                    if (me.Texture != null)
                    {
                        string t = Path.Combine(l.Folder, me.Texture);
                        if (File.Exists(t)) r.Add(t);
                    }
            }
            foreach (SoundRef s in l.Sounds)
            {
                if (s.ResolvedPath == null || !s.ResolvedPath.StartsWith(l.Folder, StringComparison.OrdinalIgnoreCase)) continue;
                r.Add(s.ResolvedPath);
                SmsFile sms = SmsFile.Load(s.ResolvedPath);
                foreach (SmsGroup g in sms.Groups) foreach (SmsStream st in g.Streams) foreach (string f in st.AllFiles())
                {
                    string w = sms.ResolveWav(f, null);
                    if (w == null) continue;
                    w = Path.GetFullPath(w);
                    if (w.StartsWith(l.Folder, StringComparison.OrdinalIgnoreCase)) r.Add(w);
                }
            }
            string cab = Path.Combine(l.Folder, "CABVIEW");
            if (Directory.Exists(cab)) foreach (string f in Directory.GetFiles(cab, "*", SearchOption.AllDirectories)) r.Add(f);
            return new List<string>(r);
        }
    }
}
