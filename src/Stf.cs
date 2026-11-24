// Loco Tester - MSTS / Open Rails text file ("STF") reader.
// Handles UTF-16 (with BOM) and ANSI files, the SIMISA header line,
// quoted strings, comments and unbalanced brackets (damaged files).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace LocoTester
{
    public class StfNode
    {
        public string Name;
        public string Label;                           // e.g. matrix MAIN ( ... ) -> Name=matrix, Label=MAIN
        public List<object> Items = new List<object>(); // string or StfNode
        public bool Closed = true;                     // false if file ended before ')'
        public StfNode Parent;

        public StfNode(string name) { Name = name; }

        public IEnumerable<StfNode> Children()
        {
            foreach (object o in Items) { StfNode n = o as StfNode; if (n != null) yield return n; }
        }

        public IEnumerable<StfNode> Children(string name)
        {
            foreach (StfNode n in Children())
                if (string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)) yield return n;
        }

        public StfNode Child(string name)
        {
            foreach (StfNode n in Children(name)) return n;
            return null;
        }

        /// <summary>Depth-first search for every node with the given name.</summary>
        public IEnumerable<StfNode> Find(string name)
        {
            foreach (StfNode n in Children())
            {
                if (string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase)) yield return n;
                foreach (StfNode m in n.Find(name)) yield return m;
            }
        }

        public List<string> Values()
        {
            List<string> r = new List<string>();
            foreach (object o in Items) { string s = o as string; if (s != null) r.Add(s); }
            return r;
        }

        public string Value(int i)
        {
            List<string> v = Values();
            return i < v.Count ? v[i] : null;
        }

        public double Num(int i, double def)
        {
            return Stf.ParseNumber(Value(i), def);
        }
    }

    public static class Stf
    {
        /// <summary>Reads a text file the way MSTS writes them; detects UTF-16 or ANSI.</summary>
        public static string ReadText(string path, out Encoding enc)
        {
            byte[] b = File.ReadAllBytes(path);
            return DecodeText(b, out enc);
        }

        public static string DecodeText(byte[] b, out Encoding enc)
        {
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { enc = new UnicodeEncoding(false, true); return Encoding.Unicode.GetString(b, 2, b.Length - 2); }
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { enc = new UnicodeEncoding(true, true); return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2); }
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { enc = new UTF8Encoding(true); return Encoding.UTF8.GetString(b, 3, b.Length - 3); }
            // UTF-16 without BOM: many zero bytes at odd positions
            if (b.Length > 8 && b[1] == 0 && b[3] == 0 && b[0] != 0) { enc = new UnicodeEncoding(false, true); return Encoding.Unicode.GetString(b); }
            enc = Encoding.Default;
            return Encoding.Default.GetString(b);
        }

        public static void WriteText(string path, string text, Encoding enc)
        {
            File.WriteAllBytes(path, Encode(text, enc));
        }

        public static byte[] Encode(string text, Encoding enc)
        {
            if (enc == null) enc = new UnicodeEncoding(false, true);
            byte[] pre = enc.GetPreamble();
            byte[] body = enc.GetBytes(text);
            byte[] r = new byte[pre.Length + body.Length];
            Buffer.BlockCopy(pre, 0, r, 0, pre.Length);
            Buffer.BlockCopy(body, 0, r, pre.Length, body.Length);
            return r;
        }

        public static double ParseNumber(string s, double def)
        {
            if (string.IsNullOrEmpty(s)) return def;
            int i = 0;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == '-' || s[i] == '+' || s[i] == 'e' || s[i] == 'E'))
            {
                if ((s[i] == 'e' || s[i] == 'E') && (i + 1 >= s.Length || !(char.IsDigit(s[i + 1]) || s[i + 1] == '-' || s[i + 1] == '+'))) break;
                i++;
            }
            double v;
            if (i > 0 && double.TryParse(s.Substring(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            return def;
        }

        /// <summary>Number with MSTS unit suffix converted to SI (m, m/s, W, kg ...).</summary>
        public static double ParseUnit(string s, double def)
        {
            if (string.IsNullOrEmpty(s)) return def;
            double v = ParseNumber(s, double.NaN);
            if (double.IsNaN(v)) return def;
            string u = s.ToLowerInvariant();
            if (u.EndsWith("mph")) return v * 0.44704;
            if (u.EndsWith("kph") || u.EndsWith("km/h") || u.EndsWith("kmh")) return v / 3.6;
            if (u.EndsWith("in")) return v * 0.0254;
            if (u.EndsWith("ft")) return v * 0.3048;
            if (u.EndsWith("cm")) return v * 0.01;
            if (u.EndsWith("mm")) return v * 0.001;
            if (u.EndsWith("kw")) return v * 1000;
            if (u.EndsWith("hp")) return v * 745.7;
            return v;
        }

        // ---------------- tokenizer + parser ----------------

        public static StfNode Parse(string text) { return Parse(text, null); }

        /// <summary>
        /// schema (optional): child name (lower case) -> allowed parent names (lower case).
        /// When a block opens under the wrong parent (because a ')' was missing or extra),
        /// it is moved to the nearest allowed parent: up the open chain, or down into the
        /// most recent child. This rebuilds the intended structure of damaged files.
        /// </summary>
        public static StfNode Parse(string text, Dictionary<string, string[]> schema)
        {
            StfNode root = new StfNode("(root)");
            StfNode cur = root;
            Rehomed = 0;
            StfNode lastClosed = null;
            string pendingWord = null;
            int i = 0, n = text.Length;
            // skip SIMISA header line
            if (text.StartsWith("SIMISA", StringComparison.Ordinal))
            {
                int nl = text.IndexOf('\n');
                i = nl < 0 ? n : nl + 1;
            }
            while (i < n)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c) || c == '\0') { i++; continue; }
                if (c == '(')
                {
                    string nm = pendingWord ?? "";
                    pendingWord = null;
                    i++;
                    // comment / skip blocks: swallow their content, keep nothing
                    if (IsSkipName(nm)) { i = SkipBlock(text, i); continue; }
                    if (schema != null) cur = Rehome(cur, nm, schema, lastClosed);
                    lastClosed = null;
                    StfNode child = new StfNode(nm);
                    child.Parent = cur;
                    child.Closed = false;
                    cur.Items.Add(child);
                    cur = child;
                    continue;
                }
                if (c == ')')
                {
                    if (pendingWord != null) { cur.Items.Add(pendingWord); pendingWord = null; }
                    if (cur.Parent != null) { cur.Closed = true; lastClosed = cur; cur = cur.Parent; }
                    i++;
                    continue;
                }
                string tok;
                if (c == '"')
                {
                    int j = i + 1;
                    StringBuilder sb = new StringBuilder();
                    while (j < n && text[j] != '"')
                    {
                        if (text[j] == '\\' && j + 1 < n && text[j + 1] == 'n') { sb.Append('\n'); j += 2; continue; }
                        sb.Append(text[j]); j++;
                    }
                    tok = sb.ToString();
                    i = j + 1;
                    // quoted strings may be joined with '+'
                    if (pendingWord != null) { cur.Items.Add(pendingWord); }
                    pendingWord = tok;
                    continue;
                }
                {
                    int j = i;
                    while (j < n && !char.IsWhiteSpace(text[j]) && text[j] != '(' && text[j] != ')' && text[j] != '"') j++;
                    tok = text.Substring(i, j - i);
                    i = j;
                    if (tok == "+") continue;
                    if (pendingWord != null) cur.Items.Add(pendingWord);
                    pendingWord = tok;
                }
            }
            if (pendingWord != null) cur.Items.Add(pendingWord);
            return root;
        }

        [ThreadStatic] public static int Rehomed;   // blocks moved by the last schema parse on this thread

        static StfNode Rehome(StfNode cur, string name, Dictionary<string, string[]> schema, StfNode lastClosed)
        {
            string[] parents;
            if (!schema.TryGetValue(name.ToLowerInvariant(), out parents)) return cur;
            if (Allowed(cur, parents)) return cur;
            // the block just closed may hold it: that ')' was one too many -> reopen it
            if (lastClosed != null && lastClosed.Parent == cur && Allowed(lastClosed, parents)) { Rehomed++; lastClosed.Closed = false; return lastClosed; }
            // down: the latest child chain (an extra ')' closed it too early)
            {
                StfNode d0 = cur;
                for (int depth = 0; depth < 8; depth++)
                {
                    StfNode last = null;
                    for (int k = d0.Items.Count - 1; k >= 0; k--) { last = d0.Items[k] as StfNode; if (last != null) break; }
                    if (last == null) break;
                    if (Allowed(last, parents) && last.Name.ToLowerInvariant() != name.ToLowerInvariant()) { Rehomed++; last.Closed = false; return last; }
                    d0 = last;
                }
            }
            // up: an open ancestor that may hold this block (a ')' is missing)
            for (StfNode a = cur.Parent; a != null; a = a.Parent)
                if (Allowed(a, parents)) { Rehomed++; for (StfNode x = cur; x != a; x = x.Parent) x.Closed = true; return a; }
            // down: the latest child chain (an extra ')' closed it too early)
            StfNode d = cur;
            for (int depth = 0; depth < 8; depth++)
            {
                StfNode last = null;
                for (int k = d.Items.Count - 1; k >= 0; k--) { last = d.Items[k] as StfNode; if (last != null) break; }
                if (last == null) break;
                if (Allowed(last, parents)) { Rehomed++; return last; }
                d = last;
            }
            return cur;
        }

        static bool Allowed(StfNode n, string[] parents)
        {
            string p = (n.Name ?? "").ToLowerInvariant();
            foreach (string s in parents) if (s == p) return true;
            return false;
        }

        static bool IsSkipName(string s)
        {
            if (s == null) return false;
            string l = s.ToLowerInvariant();
            return l == "comment" || l == "skip" || l == "_skip" || l == "_info" || l.StartsWith("#");
        }

        static int SkipBlock(string text, int i)
        {
            int depth = 1;
            bool q = false;
            while (i < text.Length && depth > 0)
            {
                char c = text[i];
                if (c == '"') q = !q;
                else if (!q && c == '(') depth++;
                else if (!q && c == ')') depth--;
                i++;
            }
            return i;
        }

        /// <summary>Counts brackets outside quotes. Returns open-minus-close; also the
        /// index of the first ')' that has no matching '('.</summary>
        public static int BracketBalance(string text, out int strayCloseAt)
        {
            int depth = 0; bool q = false; strayCloseAt = -1;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"') q = !q;
                else if (c == '\n') q = false;
                else if (!q && c == '(') depth++;
                else if (!q && c == ')') { depth--; if (depth < 0 && strayCloseAt < 0) strayCloseAt = i; }
            }
            return depth;
        }
    }

    /// <summary>Reads the SIMISA container used by .s and .ace files (plain or zlib-compressed).</summary>
    public static class Simis
    {
        public static byte[] ReadPayload(string path, out string kind)
        {
            byte[] b = File.ReadAllBytes(path);
            string hdr = b.Length >= 8 ? Encoding.ASCII.GetString(b, 0, 8) : "";
            if (hdr == "SIMISA@F")
            {
                using (MemoryStream ms = new MemoryStream(b, 18, b.Length - 18))
                using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
                using (MemoryStream o = new MemoryStream(Math.Max(1024, BitConverter.ToInt32(b, 8))))
                {
                    ds.CopyTo(o);
                    b = o.ToArray();
                }
                kind = "compressed";
                return b;
            }
            if (hdr == "SIMISA@@")
            {
                byte[] r = new byte[b.Length - 16];
                Buffer.BlockCopy(b, 16, r, 0, r.Length);
                kind = "plain";
                return r;
            }
            kind = "unknown";
            return b;
        }
    }
}
