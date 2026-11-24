// Loco Tester - diagnose and auto-repair locomotive sound setup (.eng sound lines, .sms files, .wav references).
// Every repair makes a backup first in <loco folder>\LocoTester_Backup\<time>\.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LocoTester
{
    public enum Severity { Info, Warning, Error }

    public class Issue
    {
        public Severity Severity;
        public string Code;
        public string Message;
        public string File;
        public bool Fixable;
        public override string ToString() { return "[" + Severity + "] " + Message; }
    }

    public class RepairResult
    {
        public List<string> Actions = new List<string>();
        public List<Issue> Before = new List<Issue>();
        public List<Issue> After = new List<Issue>();
        public string BackupFolder;
        public int ErrorsBefore { get { return Count(Before, Severity.Error); } }
        public int ErrorsAfter { get { return Count(After, Severity.Error); } }
        static int Count(List<Issue> l, Severity s) { int n = 0; foreach (Issue i in l) if (i.Severity == s) n++; return n; }
    }

    public static class Doctor
    {
        public const string SmsHeader = "SIMISA@@@@@@@@@@JINX0x1t______";

        // ---------------------------------------------------------------- diagnose

        public static List<Issue> Diagnose(Loco l, string orRoot)
        {
            List<Issue> r = new List<Issue>();
            if (l.ParseError != null) Add(r, Severity.Error, "ENG_PARSE", "The .eng file could not be read: " + l.ParseError, l.EngPath, false);
            if (l.ShapeFile == null) Add(r, Severity.Warning, "NO_SHAPE", "No WagonShape line in the .eng", l.EngPath, false);
            else if (l.ShapePath == null) Add(r, Severity.Warning, "SHAPE_MISSING", "3D shape not found: " + l.ShapeFile, l.EngPath, false);

            bool hasWagonSound = false, hasEngineSound = false;
            foreach (SoundRef s in l.Sounds) { if (s.Section == "Wagon") hasWagonSound = true; else hasEngineSound = true; }
            if (l.Sounds.Count == 0) Add(r, Severity.Error, "NO_SOUND_LINE", "The .eng has no Sound ( ) line, so the loco is silent", l.EngPath, true);
            else
            {
                if (!hasWagonSound) Add(r, Severity.Warning, "NO_EXT_SOUND", "No outside (Wagon) sound line in the .eng", l.EngPath, true);
                if (!hasEngineSound) Add(r, Severity.Warning, "NO_CAB_SOUND", "No cab (Engine) sound line in the .eng", l.EngPath, true);
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SoundRef s in l.Sounds)
            {
                if (s.ResolvedPath == null) { Add(r, Severity.Error, "SMS_MISSING", "Sound file not found: " + s.FileName + " (" + s.Section + " section)", l.EngPath, true); continue; }
                if (!seen.Add(s.ResolvedPath)) continue;
                DiagnoseSms(s.ResolvedPath, orRoot, r);
            }
            for (int i = 0; i < l.CabViews.Count; i++)
                if (l.CabViewPath(i) == null) Add(r, Severity.Warning, "CAB_MISSING", "Cab view file not found: CABVIEW\\" + l.CabViews[i], l.EngPath, false);
            return r;
        }

        public static void DiagnoseSms(string path, string orRoot, List<Issue> r)
        {
            string name = Path.GetFileName(path);
            byte[] raw = File.ReadAllBytes(path);
            if (raw.Length < 20) { Add(r, Severity.Error, "SMS_EMPTY", name + " is empty or almost empty", path, true); return; }
            Encoding enc;
            string text = Stf.DecodeText(raw, out enc);
            int garbage = CountGarbage(text);
            if (garbage > 0) Add(r, Severity.Error, "SMS_GARBAGE", name + " contains " + garbage + " corrupt characters", path, true);
            if (!text.TrimStart().StartsWith("SIMISA", StringComparison.Ordinal)) Add(r, Severity.Warning, "SMS_NO_HEADER", name + " is missing its SIMISA header line", path, true);
            int stray;
            int bal = Stf.BracketBalance(text, out stray);
            if (bal > 0) Add(r, Severity.Error, "SMS_BRACKETS", name + ": " + bal + " bracket(s) never closed (file cut short or damaged)", path, true);
            if (stray >= 0) Add(r, Severity.Error, "SMS_BRACKETS", name + ": extra ')' with no matching '(' near line " + LineOf(text, stray), path, true);

            SmsFile sms = new SmsFile();
            sms.Path = path; sms.Folder = Path.GetDirectoryName(path);
            try { sms.ParseText(text); } catch (Exception ex) { sms.Error = ex.Message; }
            if (sms.Error != null) { Add(r, Severity.Error, "SMS_STRUCTURE", name + ": " + sms.Error, path, true); return; }
            if (bal == 0 && stray < 0 && sms.Rehomed > 0) Add(r, Severity.Error, "SMS_BRACKETS", name + ": " + sms.Rehomed + " block(s) are inside the wrong section (misplaced brackets)", path, true);
            int streams = 0;
            for (int gi = 0; gi < sms.Groups.Count; gi++)
            {
                SmsGroup g = sms.Groups[gi];
                string where = sms.Groups.Count > 1 ? " (group " + (gi + 1) + ")" : "";
                streams += g.Streams.Count;
                if (g.DeclaredStreamCount >= 0 && g.DeclaredStreamCount != g.Streams.Count)
                    Add(r, Severity.Warning, "SMS_COUNT", name + where + ": Streams ( " + g.DeclaredStreamCount + " ) declared but " + g.Streams.Count + " found", path, true);
                foreach (SmsStream s in g.Streams)
                {
                    if (s.DeclaredTriggerCount >= 0 && s.DeclaredTriggerCount != s.Triggers.Count)
                        Add(r, Severity.Warning, "SMS_COUNT", name + where + ": stream " + (s.Index + 1) + " declares " + s.DeclaredTriggerCount + " triggers but has " + s.Triggers.Count, path, true);
                    if (s.Triggers.Count == 0)
                        Add(r, Severity.Warning, "SMS_EMPTY_STREAM", name + where + ": stream " + (s.Index + 1) + " has no triggers and never plays", path, true);
                }
            }
            if (streams == 0) Add(r, Severity.Error, "SMS_NO_STREAMS", name + " has no sound streams", path, true);

            HashSet<string> wavSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SmsGroup g in sms.Groups)
                foreach (SmsStream s in g.Streams)
                    foreach (string f in s.AllFiles())
                    {
                        if (!wavSeen.Add(f)) continue;
                        string wp = sms.ResolveWav(f, orRoot);
                        if (wp == null) { Add(r, Severity.Error, "WAV_MISSING", name + ": sound file missing: " + f, path, true); continue; }
                        string werr = WavData.CheckHeader(wp);
                        if (werr != null) Add(r, Severity.Error, "WAV_BAD", f + ": " + werr, wp, false);
                    }
        }

        static void Add(List<Issue> r, Severity s, string code, string msg, string file, bool fixable)
        {
            Issue i = new Issue(); i.Severity = s; i.Code = code; i.Message = msg; i.File = file; i.Fixable = fixable;
            r.Add(i);
        }

        static int CountGarbage(string t)
        {
            int n = 0;
            foreach (char c in t) if ((c < 32 && c != '\r' && c != '\n' && c != '\t') || c == '�' || (c >= 0xD800 && c <= 0xDFFF)) n++;
            return n;
        }

        static int LineOf(string t, int idx)
        {
            int n = 1;
            for (int i = 0; i < idx && i < t.Length; i++) if (t[i] == '\n') n++;
            return n;
        }

        // ---------------------------------------------------------------- repair

        /// <param name="donors">Healthy locos to borrow sounds from (same engine type preferred).</param>
        public static RepairResult Repair(Loco l, string orRoot, IList<Loco> donors)
        {
            RepairResult res = new RepairResult();
            res.Before = Diagnose(l, orRoot);
            bool any = false;
            foreach (Issue i in res.Before) if (i.Fixable) any = true;
            if (!any) { res.After = res.Before; res.Actions.Add("Nothing to repair."); return res; }

            res.BackupFolder = Backup(l);
            res.Actions.Add("Backup saved to " + res.BackupFolder);
            string soundDir = Path.Combine(l.Folder, "SOUND");

            // 1. .eng sound lines
            string engText; Encoding engEnc;
            engText = Stf.ReadText(l.EngPath, out engEnc);
            bool engChanged = false;
            bool hasWagon = false, hasEngine = false;
            foreach (SoundRef s in l.Sounds) { if (s.Section == "Wagon") hasWagon = true; else hasEngine = true; }
            if (!hasWagon || !hasEngine)
            {
                string ext, cab;
                PickLocalSms(soundDir, out ext, out cab);
                Loco donor = PickDonor(l, donors, orRoot);
                if (!hasWagon)
                {
                    if (ext == null && donor != null) ext = CopyDonorSms(donor, false, soundDir, null, orRoot, res);
                    if (ext != null) { engText = InsertSoundLine(engText, "Wagon", ext); engChanged = true; res.Actions.Add("Added outside sound line: Sound ( \"" + ext + "\" )"); }
                }
                if (!hasEngine)
                {
                    if (cab == null && donor != null) cab = CopyDonorSms(donor, true, soundDir, null, orRoot, res);
                    if (cab != null) { engText = InsertSoundLine(engText, "Engine", cab); engChanged = true; res.Actions.Add("Added cab sound line: Sound ( \"" + cab + "\" )"); }
                }
            }
            if (engChanged) Stf.WriteText(l.EngPath, engText, engEnc);

            // 2. missing .sms files
            Loco fresh = Loco.Load(l.EngPath, orRoot);
            foreach (SoundRef s in fresh.Sounds)
            {
                if (s.ResolvedPath != null) continue;
                string similar = FindSimilar(soundDir, s.FileName, "*.sms", 0.6);
                if (similar != null)
                {
                    engText = Stf.ReadText(l.EngPath, out engEnc);
                    engText = ReplaceQuotedOrBare(engText, s.FileName, similar);
                    Stf.WriteText(l.EngPath, engText, engEnc);
                    res.Actions.Add("Sound line pointed at existing file " + similar + " (was " + s.FileName + ")");
                    continue;
                }
                Loco donor = PickDonor(l, donors, orRoot);
                if (donor != null)
                {
                    string made = CopyDonorSms(donor, s.Section == "Engine", soundDir, s.FileName, orRoot, res);
                    if (made != null) continue;
                }
                res.Actions.Add("Could not rebuild " + s.FileName + ": no healthy loco to copy sounds from");
            }

            // 3. fix each .sms
            fresh = Loco.Load(l.EngPath, orRoot);
            HashSet<string> done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SoundRef s in fresh.Sounds)
            {
                if (s.ResolvedPath == null || !done.Add(s.ResolvedPath)) continue;
                // only touch files inside this loco's folder; never edit shared global sounds
                if (!s.ResolvedPath.StartsWith(l.Folder, StringComparison.OrdinalIgnoreCase)) continue;
                RepairSms(l, s, orRoot, donors, res);
            }

            res.After = Diagnose(Loco.Load(l.EngPath, orRoot), orRoot);
            return res;
        }

        static void RepairSms(Loco l, SoundRef sref, string orRoot, IList<Loco> donors, RepairResult res)
        {
            string path = sref.ResolvedPath;
            string name = Path.GetFileName(path);
            byte[] raw = File.ReadAllBytes(path);
            Encoding enc = new UnicodeEncoding(false, true);
            string text = raw.Length > 0 ? Stf.DecodeText(raw, out enc) : null;
            if (text == null || text.Trim().Length < 20)
            {
                ReplaceWithDonor(l, sref, orRoot, donors, res, name + " was empty");
                return;
            }
            string orig = text;

            // a) strip corrupt characters
            StringBuilder sb = new StringBuilder(text.Length);
            int removed = 0;
            foreach (char c in text)
            {
                if ((c < 32 && c != '\r' && c != '\n' && c != '\t') || c == '�' || (c >= 0xD800 && c <= 0xDFFF)) { removed++; continue; }
                sb.Append(c);
            }
            if (removed > 0) { text = sb.ToString(); res.Actions.Add(name + ": removed " + removed + " corrupt characters"); }

            // b) header
            if (!text.TrimStart().StartsWith("SIMISA", StringComparison.Ordinal))
            {
                text = SmsHeader + "\r\n\r\n" + text.TrimStart();
                res.Actions.Add(name + ": restored SIMISA header line");
            }

            // c) structure: brackets, counts, missing files -> rebuild from the parsed tree
            int stray;
            int bal = Stf.BracketBalance(text, out stray);
            StfNode root = Stf.Parse(text, SmsFile.Schema);
            int rehomed = Stf.Rehomed;
            StfNode trsms = root.Child("Tr_SMS");
            if (trsms == null || CountStreams(trsms) == 0)
            {
                ReplaceWithDonor(l, sref, orRoot, donors, res, name + " has no usable sound streams");
                return;
            }
            bool rebuild = bal != 0 || stray >= 0 || rehomed > 0;
            if (bal > 0) res.Actions.Add(name + ": file was cut short or lost brackets; closed " + bal + " open bracket(s)");
            if (stray >= 0 || bal < 0) res.Actions.Add(name + ": removed extra ')' bracket(s)");
            if (rehomed > 0) res.Actions.Add(name + ": put " + rehomed + " misplaced block(s) back where they belong");
            int droppedStreams = DropIncomplete(trsms);
            if (droppedStreams > 0) res.Actions.Add(name + ": removed " + droppedStreams + " incomplete stream(s)");

            // missing wav files
            string folder = Path.GetDirectoryName(path);
            Loco donor = PickDonor(l, donors, orRoot);
            SmsFile donorSms = null;
            if (donor != null) { string dp = donor.SmsPath(sref.Section == "Engine"); if (dp != null) donorSms = SmsFile.Load(dp); }
            foreach (StfNode fileNode in new List<StfNode>(trsms.Find("File")))
            {
                string f = fileNode.Value(0);
                if (f == null) continue;
                if (ResolveWav(folder, f, orRoot) != null) continue;
                string sim = FindSimilar(folder, f, "*.wav", 0.75);
                if (sim != null)
                {
                    SetFirstValue(fileNode, sim);
                    res.Actions.Add(name + ": " + f + " not found, using " + sim + " (similar name)");
                    rebuild = true; continue;
                }
                string fromDonor = DonorWavFor(fileNode, donorSms, orRoot);
                if (fromDonor != null && f.IndexOfAny(Path.GetInvalidPathChars()) < 0)
                {
                    string target = Path.Combine(folder, f);
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Copy(fromDonor, target, false);
                        res.Actions.Add(name + ": rebuilt missing " + f + " from " + donor.DisplayName + " (" + Path.GetFileName(fromDonor) + ")");
                        continue;
                    }
                    catch (Exception ex) { res.Actions.Add(name + ": could not copy donor sound: " + ex.Message); }
                }
                // last resort: remove the reference
                RemoveFileRef(fileNode);
                res.Actions.Add(name + ": removed reference to missing " + f);
                rebuild = true;
            }

            // counts
            if (FixCounts(trsms)) { rebuild = true; res.Actions.Add(name + ": corrected Streams / Triggers / CurvePoints counts"); }

            if (rebuild || droppedStreams > 0)
            {
                text = SmsHeader + "\r\n\r\n" + Serialize(trsms, 0);
            }
            if (text != orig)
            {
                Stf.WriteText(path, text, enc ?? new UnicodeEncoding(false, true));
            }
        }

        static int CountStreams(StfNode trsms)
        {
            int n = 0;
            foreach (StfNode s in trsms.Find("Stream")) n++;
            return n;
        }

        /// <summary>Removes streams/triggers/curves that were cut off before their closing bracket.</summary>
        static int DropIncomplete(StfNode trsms)
        {
            int dropped = 0;
            foreach (StfNode streams in new List<StfNode>(trsms.Find("Streams")))
            {
                foreach (StfNode s in new List<StfNode>(streams.Children("Stream")))
                {
                    StfNode tr = s.Child("Triggers");
                    if (!IsComplete(s) || tr == null || CountChildren(tr, null) == 0) { streams.Items.Remove(s); dropped++; }
                }
            }
            // mark everything closed now that broken tails are gone
            MarkClosed(trsms);
            return dropped;
        }

        static bool IsComplete(StfNode n)
        {
            if (!n.Closed) return false;
            foreach (StfNode c in n.Children()) if (!IsComplete(c)) return false;
            return true;
        }

        static void MarkClosed(StfNode n) { n.Closed = true; foreach (StfNode c in n.Children()) MarkClosed(c); }

        static bool FixCounts(StfNode trsms)
        {
            bool changed = false;
            foreach (StfNode streams in trsms.Find("Streams")) changed |= SetCount(streams, CountChildren(streams, "Stream"));
            foreach (StfNode tr in trsms.Find("Triggers")) changed |= SetCount(tr, CountChildren(tr, null));
            foreach (StfNode cp in trsms.Find("CurvePoints"))
            {
                int vals = cp.Values().Count - 1;
                if (vals >= 0) changed |= SetCount(cp, vals / 2);
            }
            return changed;
        }

        static int CountChildren(StfNode n, string name)
        {
            int c = 0;
            foreach (StfNode k in n.Children()) if (name == null || string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase)) c++;
            return c;
        }

        static bool SetCount(StfNode n, int count)
        {
            for (int i = 0; i < n.Items.Count; i++)
            {
                string s = n.Items[i] as string;
                if (s != null)
                {
                    if (s == count.ToString(CultureInfo.InvariantCulture)) return false;
                    n.Items[i] = count.ToString(CultureInfo.InvariantCulture);
                    return true;
                }
            }
            n.Items.Insert(0, count.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        static void SetFirstValue(StfNode n, string v)
        {
            for (int i = 0; i < n.Items.Count; i++) if (n.Items[i] is string) { n.Items[i] = v; return; }
        }

        /// <summary>Remove a File( ) entry; drop the trigger / stream if nothing is left to play.</summary>
        static void RemoveFileRef(StfNode fileNode)
        {
            StfNode cmd = fileNode.Parent;
            if (cmd == null) return;
            cmd.Items.Remove(fileNode);
            // the numeric "count" before File entries in StartLoop ( n File(..) ...)
            int files = CountChildren(cmd, "File");
            SetCount(cmd, files);
            if (files > 0) return;
            StfNode trig = cmd.Parent;
            if (trig == null || trig.Parent == null) return;
            StfNode triggers = trig.Parent;
            triggers.Items.Remove(trig);
            if (CountChildren(triggers, null) > 0) return;
            StfNode stream = triggers.Parent;
            if (stream != null && stream.Parent != null) stream.Parent.Items.Remove(stream);
        }

        static string DonorWavFor(StfNode fileNode, SmsFile donorSms, string orRoot)
        {
            if (donorSms == null) return null;
            // what trigger plays this file?
            StfNode trig = fileNode.Parent != null ? fileNode.Parent.Parent : null;
            int discrete = -1; string variable = null;
            if (trig != null)
            {
                string tn = (trig.Name ?? "").ToLowerInvariant();
                if (tn == "discrete_trigger") discrete = (int)trig.Num(0, -1);
                else if (tn == "variable_trigger") { string w = trig.Value(0) ?? ""; int us = w.IndexOf('_'); variable = us > 0 ? w.Substring(0, us) : w; }
            }
            string want = fileNode.Value(0) ?? "";
            string best = null; double bestScore = -1;
            foreach (SmsGroup g in donorSms.Groups)
                foreach (SmsStream s in g.Streams)
                    foreach (SmsTrigger t in s.Triggers)
                    {
                        bool match = (discrete >= 0 && t.Kind == TrigKind.Discrete && t.Discrete == discrete)
                                  || (variable != null && t.Kind == TrigKind.Variable && string.Equals(t.Variable, variable, StringComparison.OrdinalIgnoreCase));
                        foreach (string f in t.Files)
                        {
                            string p = donorSms.ResolveWav(f, orRoot);
                            if (p == null) continue;
                            double sc = (match ? 1.0 : 0.0) + Similarity(Path.GetFileNameWithoutExtension(f), Path.GetFileNameWithoutExtension(want));
                            if (sc > bestScore) { bestScore = sc; best = p; }
                        }
                    }
            return bestScore >= 0.5 ? best : null;
        }

        static void ReplaceWithDonor(Loco l, SoundRef sref, string orRoot, IList<Loco> donors, RepairResult res, string why)
        {
            Loco donor = PickDonor(l, donors, orRoot);
            if (donor == null) { res.Actions.Add(why + "; no healthy loco found to rebuild it from"); return; }
            CopyDonorSms(donor, sref.Section == "Engine", Path.GetDirectoryName(sref.ResolvedPath), Path.GetFileName(sref.ResolvedPath), orRoot, res, true);
            res.Actions.Add(why + "; replaced with the sound set of " + donor.DisplayName);
        }

        // ---------------------------------------------------------------- helpers

        public static string Backup(Loco l)
        {
            string dir = Path.Combine(Path.Combine(l.Folder, "LocoTester_Backup"), DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            File.Copy(l.EngPath, Path.Combine(dir, Path.GetFileName(l.EngPath)), true);
            string sd = Path.Combine(l.Folder, "SOUND");
            if (Directory.Exists(sd))
            {
                string bsd = Path.Combine(dir, "SOUND");
                Directory.CreateDirectory(bsd);
                foreach (string f in Directory.GetFiles(sd, "*.sms")) File.Copy(f, Path.Combine(bsd, Path.GetFileName(f)), true);
                // list of wavs present before repair (wavs themselves are never modified, only added)
                File.WriteAllLines(Path.Combine(bsd, "_wav_list.txt"), Array.ConvertAll(Directory.GetFiles(sd, "*.wav"), Path.GetFileName));
            }
            return dir;
        }

        static void PickLocalSms(string soundDir, out string ext, out string cab)
        {
            ext = null; cab = null;
            if (!Directory.Exists(soundDir)) return;
            foreach (string f in Directory.GetFiles(soundDir, "*.sms"))
            {
                SmsFile s = SmsFile.Load(f);
                bool hasCab = false, hasExt = false;
                foreach (SmsGroup g in s.Groups) { if (g.CabCam) hasCab = true; if (g.ExternalCam) hasExt = true; }
                string n = Path.GetFileName(f);
                string low = n.ToLowerInvariant();
                if (cab == null && (hasCab || low.Contains("cab") || low.Contains("int"))) cab = n;
                else if (ext == null && (hasExt || low.Contains("ext") || low.Contains("x_"))) ext = n;
            }
        }

        static readonly Dictionary<string, int> errorCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static int ErrorCount(Loco d, string orRoot)
        {
            int e;
            lock (errorCache) if (errorCache.TryGetValue(d.EngPath, out e)) return e;
            e = 0;
            foreach (Issue i in Diagnose(d, orRoot)) if (i.Severity == Severity.Error) e++;
            lock (errorCache) errorCache[d.EngPath] = e;
            return e;
        }

        public static void ForgetHealth(Loco d) { lock (errorCache) errorCache.Remove(d.EngPath); }

        /// <summary>Best loco to borrow sounds from: same engine type first, then the fewest sound errors.</summary>
        public static Loco PickDonor(Loco l, IList<Loco> donors, string orRoot)
        {
            if (donors == null) return null;
            Loco best = null; int bestScore = int.MaxValue;
            foreach (Loco d in donors)
            {
                if (d == null || d.EngPath == l.EngPath || d.Folder == l.Folder) continue;
                if (d.FolderName.StartsWith("LT_DUMMY", StringComparison.OrdinalIgnoreCase)) continue;
                if (d.SmsPath(false) == null || d.SmsPath(true) == null) continue;
                int score = ErrorCount(d, orRoot) * 10 + (string.Equals(d.EngineType, l.EngineType, StringComparison.OrdinalIgnoreCase) ? 0 : 1000);
                if (score < bestScore) { bestScore = score; best = d; if (score == 0) break; }
            }
            return best;
        }

        /// <summary>Copies the donor's .sms (+ every .wav it uses) into soundDir. Returns the new .sms file name.</summary>
        static string CopyDonorSms(Loco donor, bool cab, string soundDir, string targetName, string orRoot, RepairResult res)
        {
            return CopyDonorSms(donor, cab, soundDir, targetName, orRoot, res, false);
        }

        static string CopyDonorSms(Loco donor, bool cab, string soundDir, string targetName, string orRoot, RepairResult res, bool overwrite)
        {
            string src = donor.SmsPath(cab);
            if (src == null) return null;
            Directory.CreateDirectory(soundDir);
            string name = targetName ?? ("LT_" + Path.GetFileName(src));
            string dst = Path.Combine(soundDir, name);
            if (File.Exists(dst) && !overwrite) return name;
            File.Copy(src, dst, true);
            SmsFile s = SmsFile.Load(src);
            int copied = 0;
            foreach (SmsGroup g in s.Groups)
                foreach (SmsStream st in g.Streams)
                    foreach (string f in st.AllFiles())
                    {
                        string wp = s.ResolveWav(f, orRoot);
                        if (wp == null) continue;
                        // wavs from the global SOUND folder stay there
                        if (!wp.StartsWith(Path.GetDirectoryName(src), StringComparison.OrdinalIgnoreCase)) continue;
                        string wd = Path.Combine(soundDir, f);
                        if (File.Exists(wd)) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(wd));
                        File.Copy(wp, wd);
                        copied++;
                    }
            res.Actions.Add("Rebuilt " + name + " from " + donor.DisplayName + " (" + (cab ? "cab" : "outside") + " sounds, " + copied + " sound files copied)");
            return name;
        }

        static string ResolveWav(string folder, string f, string orRoot)
        {
            if (string.IsNullOrEmpty(f) || f.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
            string a = Path.Combine(folder, f);
            if (File.Exists(a)) return a;
            if (orRoot != null) { string b = Path.Combine(Path.Combine(orRoot, "SOUND"), f); if (File.Exists(b)) return b; }
            return null;
        }

        static string FindSimilar(string dir, string wanted, string pattern, double min)
        {
            if (!Directory.Exists(dir) || string.IsNullOrEmpty(wanted) || wanted.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
            string w = Path.GetFileNameWithoutExtension(wanted);
            string best = null; double bs = min;
            foreach (string f in Directory.GetFiles(dir, pattern))
            {
                double s = Similarity(Path.GetFileNameWithoutExtension(f), w);
                if (s >= bs) { bs = s; best = Path.GetFileName(f); }
            }
            return best;
        }

        public static double Similarity(string a, string b)
        {
            a = (a ?? "").ToLowerInvariant(); b = (b ?? "").ToLowerInvariant();
            if (a.Length == 0 && b.Length == 0) return 1;
            int[,] d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return 1.0 - (double)d[a.Length, b.Length] / Math.Max(a.Length, b.Length);
        }

        static string InsertSoundLine(string eng, string section, string sms)
        {
            // find "Wagon (" or "Engine (" at top level and insert after its first line
            Match m = Regex.Match(eng, @"(?im)^\s*" + section + @"\s*\([^\r\n]*\r?\n");
            string line = "\tSound ( \"" + sms + "\" )\r\n";
            if (!m.Success) return eng;
            return eng.Insert(m.Index + m.Length, line);
        }

        static string ReplaceQuotedOrBare(string text, string oldName, string newName)
        {
            return Regex.Replace(text, @"(Sound\s*\(\s*""?)" + Regex.Escape(oldName) + @"(""?\s*\))", "${1}" + newName.Replace("$", "$$") + "${2}", RegexOptions.IgnoreCase);
        }

        // ---------------------------------------------------------------- writer

        public static string Serialize(StfNode n, int indent)
        {
            StringBuilder sb = new StringBuilder();
            Write(n, indent, sb);
            return sb.ToString();
        }

        static void Write(StfNode n, int indent, StringBuilder sb)
        {
            string ln = (n.Name ?? "").ToLowerInvariant();
            if (ln == "comment" || ln == "skip" || ln == "_skip" || ln == "_info") return;  // contents were not kept by the parser
            string pad = new string('\t', indent);
            bool hasKids = false;
            foreach (object o in n.Items) if (o is StfNode) { hasKids = true; break; }
            sb.Append(pad).Append(n.Name).Append(" (");
            if (!hasKids)
            {
                foreach (object o in n.Items) sb.Append(' ').Append(Q((string)o, n.Name));
                sb.Append(" )\r\n");
                return;
            }
            // leading values on the same line
            int i = 0;
            for (; i < n.Items.Count && n.Items[i] is string; i++) sb.Append(' ').Append(Q((string)n.Items[i], n.Name));
            // short command blocks (StartLoop ( 1 File ( "x.wav" -1 ) SelectionMethod ( ... ) )) stay on one line
            if (IsInline(n))
            {
                for (; i < n.Items.Count; i++)
                {
                    object o = n.Items[i];
                    if (o is string) sb.Append(' ').Append(Q((string)o, n.Name));
                    else { sb.Append(' '); WriteInline((StfNode)o, sb); }
                }
                sb.Append(" )\r\n");
                return;
            }
            sb.Append("\r\n");
            for (; i < n.Items.Count; i++)
            {
                object o = n.Items[i];
                if (o is string) sb.Append(pad).Append('\t').Append(Q((string)o, n.Name)).Append("\r\n");
                else Write((StfNode)o, indent + 1, sb);
            }
            sb.Append(pad).Append(")\r\n");
        }

        static bool IsInline(StfNode n)
        {
            string l = (n.Name ?? "").ToLowerInvariant();
            return l.EndsWith("_trigger") || l == "startloop" || l == "startlooprelease" || l == "playoneshot" || l == "activation" || l == "deactivation";
        }

        static void WriteInline(StfNode n, StringBuilder sb)
        {
            sb.Append(n.Name).Append(" (");
            foreach (object o in n.Items)
            {
                if (o is string) sb.Append(' ').Append(Q((string)o, n.Name));
                else { sb.Append(' '); WriteInline((StfNode)o, sb); }
            }
            sb.Append(" )");
        }

        static string Q(string v, string parent)
        {
            double d;
            bool numeric = double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
            if (string.Equals(parent, "File", StringComparison.OrdinalIgnoreCase) && !numeric) return "\"" + v + "\"";
            if (v.Length == 0 || v.IndexOfAny(new char[] { ' ', '\t', '(', ')' }) >= 0) return "\"" + v + "\"";
            if (!numeric && v.Contains(".")) return "\"" + v + "\"";
            return v;
        }
    }
}
