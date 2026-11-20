using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace HoverTranslate
{
    /// <summary>
    /// 联网解释：把选中/悬停的文本交给模型，让它用「一句话 + 生活比方」讲清楚里面的技术名词。
    /// 凭证复用本机 DSH 的 DEEPSEEK_API_KEY，不额外申请账号。
    /// </summary>
    internal static class AiExplainer
    {
        private static bool _keyLoaded;
        private static string _key;

        private static readonly Dictionary<string, List<Terminology.Term>> Cache =
            new Dictionary<string, List<Terminology.Term>>(StringComparer.Ordinal);
        private static readonly List<string> Order = new List<string>();
        private const int CacheLimit = 120;
        private static readonly object Lock = new object();

        public static bool Available
        {
            get { return !string.IsNullOrEmpty(ApiKey); }
        }

        private static string ApiKey
        {
            get
            {
                if (_keyLoaded) return _key;
                _keyLoaded = true;
                try
                {
                    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    string path = Path.Combine(home, ".dsh", ".credentials.yaml");
                    if (!File.Exists(path)) return null;

                    foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        int idx = line.IndexOf("DEEPSEEK_API_KEY:", StringComparison.Ordinal);
                        if (idx < 0) continue;
                        string v = line.Substring(idx + "DEEPSEEK_API_KEY:".Length).Trim();
                        v = v.Trim('"').Trim('\'').Trim();
                        if (v.Length > 10) { _key = v; return _key; }
                    }
                }
                catch (Exception ex) { AppConfig.Log("ai key: " + ex.Message); }
                return _key;
            }
        }

        /// <summary>挑出文本里最难的 1-3 个词，给成人话解释。失败返回空列表。</summary>
        public static List<Terminology.Term> Explain(string text, int max)
        {
            var result = new List<Terminology.Term>();
            if (string.IsNullOrEmpty(text) || max <= 0) return result;

            string key = text.Trim();
            if (key.Length > 160) key = key.Substring(0, 160);

            lock (Lock)
            {
                List<Terminology.Term> hit;
                if (Cache.TryGetValue(key, out hit)) return hit;
            }

            string apiKey = ApiKey;
            if (string.IsNullOrEmpty(apiKey)) return result;

            try
            {
                var payload = new Dictionary<string, object>();
                payload["model"] = "deepseek-chat";
                payload["temperature"] = 0.3;
                payload["max_tokens"] = 400;
                payload["stream"] = false;
                payload["messages"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        { "role", "system" },
                        { "content",
                          "你在给完全不懂技术的人解释他看到的文字里的技术名词。" +
                          "每条格式严格为「词｜解释」，一行一条，不要编号、不要前缀、不要额外的开场白。" +
                          "解释要求：先用一句话说清它是什么（不许用别的术语来解释），再用一个日常生活里的例子打比方，" +
                          "整条不超过 70 个汉字。挑其中最可能让人看不懂的，最多 " + max + " 条。" +
                          "如果这段文字里没有任何技术名词，只输出一个词：无" }
                    },
                    new Dictionary<string, object>
                    {
                        { "role", "user" },
                        { "content", "文字：" + key }
                    }
                };

                string json = new JavaScriptSerializer().Serialize(payload);
                var headers = new Dictionary<string, string>();
                headers["Authorization"] = "Bearer " + apiKey;
                string raw = HttpUtil.PostJson("https://api.deepseek.com/chat/completions", json, 12000, headers);
                if (string.IsNullOrEmpty(raw)) return result;

                var root = JsonHelp.ParseObject(raw);
                if (root == null) return result;
                var choices = root.ContainsKey("choices") ? root["choices"] as System.Collections.IList : null;
                if (choices == null || choices.Count == 0) return result;
                var first = choices[0] as IDictionary<string, object>;
                var msg = JsonHelp.Sub(first, "message");
                string content = JsonHelp.Str(msg, "content");
                if (string.IsNullOrEmpty(content)) return result;

                foreach (string rawLine in content.Split('\n'))
                {
                    string line = rawLine.Trim();
                    if (line.Length < 4) continue;
                    if (line == "无" || line.StartsWith("无｜")) continue;

                    int sep = line.IndexOf('｜');
                    if (sep < 0) sep = line.IndexOf('|');
                    if (sep <= 0 || sep >= line.Length - 1) continue;

                    string term = line.Substring(0, sep).Trim().Trim('*', '`', '「', '」', '『', '』');
                    string explain = line.Substring(sep + 1).Trim().Trim('*', '`');
                    if (term.Length == 0 || term.Length > 24 || explain.Length < 6) continue;

                    result.Add(new Terminology.Term
                    {
                        n = term,
                        d = explain,
                        x = "",
                        c = "AI 解释"
                    });
                    if (result.Count >= max) break;
                }
            }
            catch (Exception ex)
            {
                AppConfig.Log("ai explain: " + ex.Message);
            }

            lock (Lock)
            {
                if (Cache.Count >= CacheLimit)
                {
                    int drop = Math.Max(1, CacheLimit / 4);
                    for (int i = 0; i < drop && i < Order.Count; i++) Cache.Remove(Order[i]);
                    Order.RemoveRange(0, Math.Min(drop, Order.Count));
                }
                Cache[key] = result;
                Order.Add(key);
            }

            return result;
        }
    }
}
