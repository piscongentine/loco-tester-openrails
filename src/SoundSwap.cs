// Loco Tester - replace a loco's horn / bell / sander sound with your own .wav (and put the original back).
// The new .wav is copied next to the .sms; every Discrete_Trigger of that kind in the .sms is pointed at it.
// The first time a .sms is changed, the untouched original is kept as <sms>.LocoTester_original.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LocoTester
{
    public class SwapSlot
    {
        public string Id, Title, Glyph;
        public int OnTrigger;               // trigger that starts the sound (horn 8, bell 10, sander 4)
        public List<string> CurrentFiles = new List<string>();   // files the .sms files use now
        public List<string> SmsFiles = new List<string>();       // .sms files that have this sound
        public bool Replaced;               // differs from the kept original
        public bool HasOriginal;            // an original copy exists (so Restore is possible)
    }

    public static class SoundSwap
    {
        public static readonly string[][] Kinds =
        {
            new[] { "horn", "Horn", "📯", "8" },
            new[] { "bell", "Bell / second horn", "🔔", "10" },
            new[] { "sander", "Sander", "⏬", "4" },
        };

        public static string OriginalOf(string sms) { return sms + ".LocoTester_original"; }

        // Discrete_Trigger ( N  StartLoopRelease|StartLoop|PlayOneShot ( count  File ( "x.wav" -1 ) ... )
        static Regex TriggerRx(int n)
        {
            return new Regex(@"(Discrete_Trigger\s*\(\s*" + n + @"\s+(?:StartLoopRelease|StartLoop|PlayOneShot)\s*\(\s*)(\d+)((?:\s*File\s*\(\s*""[^""]*""\s*-?\d+\s*\))+)",
                RegexOptions.IgnoreCase);
        }

        static readonly Regex FileRx = new Regex(@"File\s*\(\s*""([^""]*)""", RegexOptions.IgnoreCase);

        public static List<string> SmsOf(Loco l)
        {
            List<string> r = new List<string>();
            foreach (SoundRef s in l.Sounds) if (s.ResolvedPath != null && !r.Contains(s.ResolvedPath)) r.Add(s.ResolvedPath);
            return r;
        }

        /// <summary>The replaceable sounds this loco has (only kinds its .sms files actually use).</summary>
        public static List<SwapSlot> Slots(Loco l)
        {
            List<SwapSlot> slots = new List<SwapSlot>();
            List<string> smsList = SmsOf(l);
            foreach (string[] k in Kinds)
            {
                SwapSlot s = new SwapSlot { Id = k[0], Title = k[1], Glyph = k[2], OnTrigger = int.Parse(k[3]) };
                Regex rx = TriggerRx(s.OnTrigger);
                foreach (string sms in smsList)
                {
                    Encoding enc;
                    string text = Stf.ReadText(sms, out enc);
                    bool any = false;
                    foreach (Match m in rx.Matches(text))
                        foreach (Match f in FileRx.Matches(m.Groups[3].Value))
                        {
                            any = true;
                            if (f.Groups[1].Value.Length > 0 && !s.CurrentFiles.Contains(f.Groups[1].Value)) s.CurrentFiles.Add(f.Groups[1].Value);
                        }
                    if (!any) continue;
                    s.SmsFiles.Add(sms);
                    string orig = OriginalOf(sms);
                    if (File.Exists(orig))
                    {
                        Encoding e2;
                        if (FilesFor(Stf.ReadText(orig, out e2), s.OnTrigger) != FilesFor(text, s.OnTrigger)) { s.HasOriginal = true; s.Replaced = true; }
                    }
                }
                if (s.SmsFiles.Count > 0) slots.Add(s);
            }
            return slots;
        }

        static string FilesFor(string text, int trigger)
        {
            StringBuilder b = new StringBuilder();
            foreach (Match m in TriggerRx(trigger).Matches(text)) b.Append(m.Groups[3].Value).Append('|');
            return b.ToString();
        }

        /// <summary>Checks a .wav the user picked. Returns null when Open Rails / MSTS can play it.</summary>
        public static string Check(string wav)
        {
            if (!File.Exists(wav)) return "File not found";
            if (!wav.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return "Use a .wav file only: Open Rails and MSTS play WAV sounds only (convert MP3 / OGG to 16-bit PCM WAV first)";
            string err = WavData.CheckHeader(wav);
            if (err != null) return err + ". Save it as a PCM WAV (16-bit, mono or stereo) and try again.";
            return null;
        }

        /// <summary>Replaces the sound of one slot in every .sms of the loco. addLoop: if the file has no
        /// loop points, write two (at 20 % and 75 %) so it sustains while the button is held and plays
        /// its ending when released, like MSTS horns.</summary>
        public static string Replace(Loco l, SwapSlot slot, string wav, bool addLoop)
        {
            string err = Check(wav);
            if (err != null) throw new InvalidOperationException(err);
            WavData w = WavData.Load(wav);
            if (w.Error != null) throw new InvalidOperationException(w.Error);
            string safe = Regex.Replace(Path.GetFileNameWithoutExtension(wav), @"[^A-Za-z0-9_\-]+", "_");
            if (safe.Length > 40) safe = safe.Substring(0, 40);
            string newName = "LT_" + slot.Id + "_" + safe + ".wav";
            List<string> done = new List<string>();
            bool retuned = false;
            foreach (string sms in slot.SmsFiles)
            {
                string folder = Path.GetDirectoryName(sms);
                string dest = Path.Combine(folder, newName);
                byte[] bytes = File.ReadAllBytes(wav);
                if (addLoop && w.Cues.Count < 2) bytes = WithCues(bytes, (int)(w.Frames * 0.20), (int)(w.Frames * 0.75));
                File.WriteAllBytes(dest, bytes);
                Encoding enc;
                string text = Stf.ReadText(sms, out enc);
                string orig = OriginalOf(sms);
                if (!File.Exists(orig)) File.Copy(sms, orig);
                SmsFile sf = new SmsFile { Path = sms, Folder = folder };
                // work from the last match backwards so earlier positions stay valid
                List<Match> ms = new List<Match>();
                foreach (Match m in TriggerRx(slot.OnTrigger).Matches(text)) ms.Add(m);
                string changed = text;
                for (int i = ms.Count - 1; i >= 0; i--)
                {
                    Match m = ms[i];
                    int s0, s1;
                    if (!StreamBounds(changed, m.Index, out s0, out s1)) { s0 = m.Index; s1 = m.Index + m.Length; }
                    string block = changed.Substring(s0, s1 - s0);
                    // the old sound's sample rate: a FrequencyCurve tuned for it (e.g. a fixed 11000 Hz for an
                    // 11 kHz whistle) would play the new file too slow or too fast, so scale it to the new rate
                    int oldRate = 0;
                    foreach (Match f in FileRx.Matches(m.Groups[3].Value))
                    {
                        string op = sf.ResolveWav(f.Groups[1].Value, null);
                        if (op != null) { WavData ow = WavData.Load(op); if (ow.Error == null && ow.Rate > 0) { oldRate = ow.Rate; break; } }
                    }
                    int local = m.Index - s0;
                    string newTrig = m.Groups[1].Value + "1 File ( \"" + newName + "\" -1 )";
                    block = block.Substring(0, local) + newTrig + block.Substring(local + m.Length);
                    if (oldRate > 0 && w.Rate > 0 && oldRate != w.Rate)
                    {
                        string rb = RescaleFrequency(block, (double)w.Rate / oldRate);
                        if (rb != block) { block = rb; retuned = true; }
                    }
                    changed = changed.Substring(0, s0) + block + changed.Substring(s1);
                }
                if (changed != text) { Stf.WriteText(sms, changed, enc); done.Add(Path.GetFileName(sms)); }
            }
            return slot.Title + " replaced with " + Path.GetFileName(wav) + " in " + string.Join(", ", done.ToArray()) +
                (addLoop && w.Cues.Count < 2 ? " (loop points added: it sustains while held)" : "") +
                (retuned ? " · pitch curve adjusted to the new file's sample rate" : "");
        }

        /// <summary>Start / end of the Stream ( ... ) block that contains position pos.</summary>
        static bool StreamBounds(string text, int pos, out int start, out int end)
        {
            start = end = -1;
            int from = pos;
            while (from > 0)
            {
                int i = text.LastIndexOf("Stream", from, StringComparison.OrdinalIgnoreCase);
                if (i < 0) return false;
                int k = i + 6;
                while (k < text.Length && char.IsWhiteSpace(text[k])) k++;
                bool word = (i == 0 || !char.IsLetter(text[i - 1])) && k < text.Length && text[k] == '(';
                if (word)
                {
                    int depth = 0;
                    for (int j = k; j < text.Length; j++)
                    {
                        if (text[j] == '"') { j = text.IndexOf('"', j + 1); if (j < 0) return false; continue; }
                        if (text[j] == '(') depth++;
                        else if (text[j] == ')' && --depth == 0)
                        {
                            if (j > pos) { start = i; end = j + 1; return true; }
                            break;
                        }
                    }
                }
                from = i - 1;
            }
            return false;
        }

        static readonly Regex FreqRx = new Regex(@"(FrequencyCurve\s*\([^()]*?CurvePoints\s*\(\s*)(\d+)([^()]*)\)", RegexOptions.IgnoreCase | RegexOptions.Singleline);

        /// <summary>Multiplies the Hz values (every second number) of the block's FrequencyCurve by ratio.
        /// 0 stays 0 (it means "the file's own rate").</summary>
        static string RescaleFrequency(string block, double ratio)
        {
            return FreqRx.Replace(block, delegate(Match m)
            {
                string[] nums = m.Groups[3].Value.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                StringBuilder b = new StringBuilder();
                for (int i = 0; i < nums.Length; i++)
                {
                    string v = nums[i];
                    if (i % 2 == 1)
                    {
                        double hz = Stf.ParseNumber(v, 0);
                        if (hz != 0) v = Math.Round(hz * ratio).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                    b.Append(' ').Append(v);
                }
                return m.Groups[1].Value + m.Groups[2].Value + b.ToString() + " )";
            }, 1);
        }

        /// <summary>Puts the original sound files of one slot back (the rest of the .sms stays as it is).</summary>
        public static string Restore(Loco l, SwapSlot slot)
        {
            int n = 0;
            foreach (string sms in slot.SmsFiles)
            {
                string orig = OriginalOf(sms);
                if (!File.Exists(orig)) continue;
                Encoding enc, e2;
                string text = Stf.ReadText(sms, out enc);
                string otext = Stf.ReadText(orig, out e2);
                MatchCollection was = TriggerRx(slot.OnTrigger).Matches(otext);
                MatchCollection now = TriggerRx(slot.OnTrigger).Matches(text);
                string back = text;
                // put each whole sound stream back as it was (file names and any pitch curve), last first
                for (int i = Math.Min(was.Count, now.Count) - 1; i >= 0; i--)
                {
                    int a0, a1, b0, b1;
                    if (StreamBounds(back, now[i].Index, out a0, out a1) && StreamBounds(otext, was[i].Index, out b0, out b1))
                        back = back.Substring(0, a0) + otext.Substring(b0, b1 - b0) + back.Substring(a1);
                    else
                        back = back.Substring(0, now[i].Index) + now[i].Groups[1].Value + was[i].Groups[2].Value + was[i].Groups[3].Value + back.Substring(now[i].Index + now[i].Length);
                }
                if (back != text) { Stf.WriteText(sms, back, enc); n++; }
            }
            return n > 0 ? slot.Title + ": original sound restored" : slot.Title + ": nothing to restore";
        }

        /// <summary>Adds (or replaces) a "cue " chunk with two cue points to a RIFF/WAV file.</summary>
        static byte[] WithCues(byte[] b, int a, int c)
        {
            List<byte> outp = new List<byte>();
            for (int i = 0; i < 12; i++) outp.Add(b[i]);   // "RIFF" size "WAVE" (size fixed at the end)
            int pos = 12;
            while (pos + 8 <= b.Length)
            {
                string id = Encoding.ASCII.GetString(b, pos, 4);
                int sz = BitConverter.ToInt32(b, pos + 4);
                if (sz < 0 || pos + 8 + sz > b.Length) { sz = b.Length - pos - 8; }
                int total = 8 + sz + (sz & 1);
                if (id != "cue ")
                    for (int i = 0; i < total && pos + i < b.Length; i++) outp.Add(b[pos + i]);
                pos += total;
            }
            // cue chunk: count, then 24-byte points (id, position, "data", chunk start, block start, sample offset)
            outp.AddRange(Encoding.ASCII.GetBytes("cue "));
            outp.AddRange(BitConverter.GetBytes(4 + 2 * 24));
            outp.AddRange(BitConverter.GetBytes(2));
            int[] pts = { a, c };
            for (int k = 0; k < 2; k++)
            {
                outp.AddRange(BitConverter.GetBytes(k + 1));
                outp.AddRange(BitConverter.GetBytes(pts[k]));
                outp.AddRange(Encoding.ASCII.GetBytes("data"));
                outp.AddRange(BitConverter.GetBytes(0));
                outp.AddRange(BitConverter.GetBytes(0));
                outp.AddRange(BitConverter.GetBytes(pts[k]));
            }
            byte[] r = outp.ToArray();
            Array.Copy(BitConverter.GetBytes(r.Length - 8), 0, r, 4, 4);
            return r;
        }
    }
}
