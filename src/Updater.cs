// Loco Tester - by Piscongentine
using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace LocoTester
{
    /// <summary>Asks GitHub for the latest release and compares it with this version.
    /// The caller opens the release page only when GitHub has a newer version.</summary>
    public static class Updater
    {
        public const string Repo = "piscongentine/loco-tester-openrails";
        public const string ReleasesUrl = "https://github.com/" + Repo + "/releases/latest";

        public class Result
        {
            public string Current, Latest, Url, Published, Error;
            public bool Newer;
        }

        /// <summary>"v1.1.5" / "1.1" -> {1,1,5,0} / {1,1,0,0}, so versions compare by number, not text.</summary>
        public static int[] Parts(string v)
        {
            int[] p = new int[4];
            MatchCollection m = Regex.Matches(v ?? "", @"\d+");
            for (int i = 0; i < m.Count && i < 4; i++) int.TryParse(m[i].Value, out p[i]);
            return p;
        }

        public static int Compare(string a, string b)
        {
            int[] x = Parts(a), y = Parts(b);
            for (int i = 0; i < 4; i++) if (x[i] != y[i]) return x[i].CompareTo(y[i]);
            return 0;
        }

        public static Result Check(string current)
        {
            Result r = new Result { Current = current, Url = ReleasesUrl };
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                HttpWebRequest q = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/releases/latest");
                q.UserAgent = "LocoTester/" + current;
                q.Accept = "application/vnd.github+json";
                q.Timeout = 8000;
                q.ReadWriteTimeout = 8000;
                string json;
                using (WebResponse resp = q.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                    json = sr.ReadToEnd();
                string tag = Field(json, "tag_name");
                if (tag.Length == 0) { r.Error = "GitHub did not report a version."; return r; }
                r.Latest = tag.TrimStart('v', 'V');
                string url = Field(json, "html_url");
                if (url.StartsWith("https://github.com/" + Repo + "/releases/")) r.Url = url;
                string pub = Field(json, "published_at");
                r.Published = pub.Length >= 10 ? pub.Substring(0, 10) : "";
                r.Newer = Compare(r.Latest, current) > 0;
            }
            catch (Exception)
            {
                r.Error = "Could not reach GitHub to check for updates. Check your internet connection and try again.";
            }
            return r;
        }

        /// <summary>First top-level "name": "value" string in the GitHub reply (the release's own fields come first).</summary>
        static string Field(string json, string name)
        {
            Match m = Regex.Match(json, "\"" + name + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? m.Groups[1].Value.Replace("\\/", "/") : "";
        }
    }
}
