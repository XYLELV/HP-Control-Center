// Update check against the project's GitHub releases. Quiet by design: at most once a day, in the background,
// and a found update is only shown in 设置 (plus a small dot on the sidebar and one HP 娘 line) — never a pop-up.
using System;
using System.Net;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace HPControlPanel
{
    public static class Updater
    {
        public const string Version = "1.0.2";
        public const string Author = "HoShino_Rizu";
        // "owner/name" of the GitHub repository; empty until the project is published.
        public const string Repo = "XYLELV/HP-Control-Center";

        public class Result
        {
            public string Latest, Url, Error;
            public bool Newer;
        }

        public static string RepoUrl { get { return Repo.Length > 0 ? "https://github.com/" + Repo : ""; } }

        public static Result Check()
        {
            if (Repo.Length == 0) return new Result { Error = L.S("发布到 GitHub 后可用", "available once published on GitHub") };
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var wc = new WebClient { Encoding = System.Text.Encoding.UTF8 })
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "HPControlPanel/" + Version;   // required by the GitHub API
                    wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                    var r = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(
                        wc.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest"));
                    string tag = Convert.ToString(r["tag_name"]);
                    return new Result { Latest = tag.TrimStart('v', 'V'), Url = Convert.ToString(r["html_url"]), Newer = IsNewer(tag, Version) };
                }
            }
            catch (WebException e)
            {
                var resp = e.Response as HttpWebResponse;
                if (resp != null && resp.StatusCode == HttpStatusCode.NotFound) return new Result { Error = L.S("还没有发布版本", "no release yet") };
                return new Result { Error = L.S("暂时连不上 GitHub", "暫時連不上 GitHub", "can't reach GitHub right now") };
            }
            catch (Exception e) { App.Log(e); return new Result { Error = L.S("检查失败", "check failed") }; }
        }

        // "v1.2.0" vs "1.0.0" → true. Non-numeric parts are ignored.
        public static bool IsNewer(string tag, string current)
        {
            Version a, b;
            return System.Version.TryParse(Normalize(tag), out a) && System.Version.TryParse(Normalize(current), out b) && a > b;
        }

        static string Normalize(string v)
        {
            v = (v ?? "").Trim().TrimStart('v', 'V');
            int cut = v.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut > 0) v = v.Substring(0, cut);
            return v.Contains(".") ? v : v + ".0";
        }
    }
}
