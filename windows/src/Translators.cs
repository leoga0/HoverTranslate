using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace HoverTranslate
{
    public enum Direction
    {
        Auto = 0,
        EnToZh = 1,
        ZhToEn = 2
    }

    public class TranslationResult
    {
        public string Source = "";
        public string Target = "";
        public string From = "";
        public string To = "";
        public string Engine = "";
        public string Extra = "";
        public bool Success = false;
        public string Error = "";
    }

    internal static class Lang
    {
        private static readonly Regex Cjk = new Regex(@"[\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff\u3040-\u30ff\uac00-\ud7af]", RegexOptions.Compiled);
        private static readonly Regex Latin = new Regex(@"[A-Za-z]", RegexOptions.Compiled);

        public static bool HasCjk(string s)
        {
            return !string.IsNullOrEmpty(s) && Cjk.IsMatch(s);
        }

        public static bool HasLatin(string s)
        {
            return !string.IsNullOrEmpty(s) && Latin.IsMatch(s);
        }

        /// <summary>按用户设定与文本内容决定实际翻译方向。</summary>
        public static void Resolve(Direction d, string text, out string from, out string to)
        {
            if (d == Direction.EnToZh) { from = "en"; to = "zh"; return; }
            if (d == Direction.ZhToEn) { from = "zh"; to = "en"; return; }

            if (HasCjk(text)) { from = "zh"; to = "en"; }
            else { from = "en"; to = "zh"; }
        }

        public static string DisplayPair(string from, string to)
        {
            string a = from == "zh" ? "中文" : (from == "en" ? "英文" : from);
            string b = to == "zh" ? "中文" : (to == "en" ? "英文" : to);
            return a + " → " + b;
        }
    }

    /// <summary>文本整形：清理取词结果里的多余空白、断行、不可见字符。</summary>
    internal static class TextShape
    {
        public static string Clean(string raw, int maxLen)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string s = raw.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            s = s.Replace('\u200b', ' ').Replace('\u00a0', ' ').Replace('\u3000', ' ');
            s = Regex.Replace(s, @"\s{2,}", " ").Trim();
            s = s.Trim('|', '-', '·', '*', '>', '<', '\u2022');
            s = s.Trim();
            if (s.Length > maxLen)
            {
                int cut = maxLen;
                int p = s.LastIndexOfAny(new[] { '.', '!', '?', '\u3002', '\uff01', '\uff1f', ';', '\uff1b' }, Math.Min(maxLen - 1, s.Length - 1));
                if (p > maxLen / 3) cut = p + 1;
                s = s.Substring(0, cut).Trim() + "…";
            }
            return s;
        }

        /// <summary>是否值得送去翻译：至少有一个字母，且不是纯数字/纯符号。</summary>
        public static bool Worth(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            s = s.Trim();
            if (s.Length < 1) return false;
            bool hasLetter = Lang.HasLatin(s) || Lang.HasCjk(s);
            if (!hasLetter) return false;
            int meaningful = 0;
            foreach (char c in s) if (char.IsLetterOrDigit(c)) meaningful++;
            return meaningful >= 1;
        }
    }

    public interface ITranslator
    {
        string Name { get; }
        TranslationResult Translate(string text, string from, string to);
    }

    internal class HttpUtil
    {
        private const string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36 Edg/131.0.0.0";

        static HttpUtil()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            try { ServicePointManager.DefaultConnectionLimit = 16; } catch { }
            try { ServicePointManager.Expect100Continue = false; } catch { }
        }

        public static string PostJson(string url, string json, int timeoutMs)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            return Send(url, "POST", "application/json; charset=UTF-8", body, timeoutMs, null);
        }

        public static string PostForm(string url, string form, int timeoutMs)
        {
            byte[] body = Encoding.UTF8.GetBytes(form);
            return Send(url, "POST", "application/x-www-form-urlencoded; charset=UTF-8", body, timeoutMs, null);
        }

        public static string Get(string url, int timeoutMs, string referer)
        {
            return Send(url, "GET", null, null, timeoutMs, referer);
        }

        private static string Send(string url, string method, string contentType, byte[] body, int timeoutMs, string referer)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.UserAgent = UA;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.KeepAlive = false;
            req.Proxy = null;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            if (!string.IsNullOrEmpty(contentType)) req.ContentType = contentType;
            if (!string.IsNullOrEmpty(referer)) req.Referer = referer;

            if (body != null)
            {
                req.ContentLength = body.Length;
                using (Stream rs = req.GetRequestStream())
                {
                    rs.Write(body, 0, body.Length);
                }
            }

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream st = resp.GetResponseStream())
            {
                if (st == null) return "";
                using (StreamReader sr = new StreamReader(st, Encoding.UTF8))
                {
                    return sr.ReadToEnd();
                }
            }
        }
    }

    /// <summary>JSON 解析辅助：兼容 object[] 与 ArrayList 两种数组形态。</summary>
    internal static class JsonHelp
    {
        public static IDictionary<string, object> AsDict(object node)
        {
            return node as IDictionary<string, object>;
        }

        public static IDictionary<string, object> ParseObject(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                object o = new JavaScriptSerializer().DeserializeObject(json);
                return o as IDictionary<string, object>;
            }
            catch { return null; }
        }

        public static string FirstText(object node)
        {
            if (node == null) return null;
            string direct = node as string;
            if (direct != null) return direct;
            var list = node as System.Collections.IList;
            if (list != null && list.Count > 0 && list[0] != null) return list[0].ToString();
            var arr = node as object[];
            if (arr != null && arr.Length > 0 && arr[0] != null) return arr[0].ToString();
            return null;
        }

        public static IDictionary<string, object> Sub(IDictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key)) return null;
            return d[key] as IDictionary<string, object>;
        }

        public static string Str(IDictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return null;
            return d[key].ToString();
        }
    }

    /// <summary>腾讯交互翻译（transmart），免密钥，中英质量稳定，国内直连。</summary>
    internal class TencentTranslator : ITranslator
    {
        public string Name { get { return "腾讯交互翻译"; } }

        public TranslationResult Translate(string text, string from, string to)
        {
            string srcLang = from == "zh" ? "zh" : "en";
            string tgtLang = to == "zh" ? "zh" : "en";

            var header = new Dictionary<string, object>();
            header["fn"] = "auto_translation";
            header["client_key"] = "browser-chrome-131.0.0.0";
            header["user_key"] = "";
            header["session_uuid"] = "";
            header["request_type"] = "translate";
            header["timestamp"] = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;

            var source = new Dictionary<string, object>();
            source["lang"] = srcLang;
            source["text_list"] = new[] { text };

            var target = new Dictionary<string, object>();
            target["lang"] = tgtLang;

            var payload = new Dictionary<string, object>();
            payload["header"] = header;
            payload["type"] = "plain";
            payload["model_category"] = "normal";
            payload["source"] = source;
            payload["target"] = target;

            var ser = new JavaScriptSerializer();
            string json = ser.Serialize(payload);
            string raw = HttpUtil.PostJson("https://transmart.qq.com/api/imt", json, 8000);

            var r = new TranslationResult();
            r.Engine = Name;

            var res = JsonHelp.ParseObject(raw);
            if (res == null) { r.Error = "空响应"; return r; }

            string translated = JsonHelp.FirstText(res.ContainsKey("auto_translation") ? res["auto_translation"] : null);
            if (!string.IsNullOrEmpty(translated))
            {
                r.Target = translated.Trim();
                r.Source = text;
                r.From = from;
                r.To = to;
                r.Success = r.Target.Length > 0;
                return r;
            }

            var h = JsonHelp.Sub(res, "header");
            string code = JsonHelp.Str(h, "ret_code");
            r.Error = code != null ? "ret_code=" + code : "未解析到译文";
            return r;
        }
    }

    /// <summary>有道翻译 demo 接口，作为备用。</summary>
    internal class YoudaoTranslator : ITranslator
    {
        public string Name { get { return "有道翻译"; } }

        public TranslationResult Translate(string text, string from, string to)
        {
            string lc = from == "zh" ? "zh-CHS" : "en";
            string form = "q=" + Uri.EscapeDataString(text) + "&from=Auto&to=Auto&salt=&sign=&lss=" + Uri.EscapeDataString(lc);
            string raw = HttpUtil.PostForm("https://aidemo.youdao.com/trans", form, 8000);
            var r = new TranslationResult();
            r.Engine = Name;

            if (string.IsNullOrEmpty(raw)) { r.Error = "空响应"; return r; }

            var res = JsonHelp.ParseObject(raw);
            if (res == null) { r.Error = "响应解析失败"; return r; }

            string translated = JsonHelp.FirstText(res.ContainsKey("translation") ? res["translation"] : null);
            if (!string.IsNullOrEmpty(translated))
            {
                r.Target = translated.Trim();
                r.Source = text;
                r.From = from;
                r.To = to;
                r.Success = r.Target.Length > 0;
                return r;
            }

            string ec = JsonHelp.Str(res, "errorCode");
            r.Error = (ec != null && ec != "0") ? "errorCode=" + ec : "未解析到译文";
            return r;
        }
    }

    /// <summary>MyMemory，公开记忆库翻译，第三备用。</summary>
    internal class MyMemoryTranslator : ITranslator
    {
        public string Name { get { return "MyMemory"; } }

        public TranslationResult Translate(string text, string from, string to)
        {
            string pair = (from == "zh" ? "zh-CN" : "en") + "|" + (to == "zh" ? "zh-CN" : "en");
            string url = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(text) + "&langpair=" + Uri.EscapeDataString(pair);
            string raw = HttpUtil.Get(url, 9000, null);
            var r = new TranslationResult();
            r.Engine = Name;
            if (string.IsNullOrEmpty(raw)) { r.Error = "空响应"; return r; }

            var res = JsonHelp.ParseObject(raw);
            if (res == null) { r.Error = "响应解析失败"; return r; }

            var d = JsonHelp.Sub(res, "responseData");
            string s = JsonHelp.Str(d, "translatedText");
            if (!string.IsNullOrEmpty(s))
            {
                s = s.Trim();
                if (s.Length > 0 && !s.ToUpperInvariant().Contains("QUERY LENGTH LIMIT") &&
                    !s.ToUpperInvariant().Contains("MYMEMORY WARNING"))
                {
                    r.Target = s;
                    r.Source = text;
                    r.From = from;
                    r.To = to;
                    r.Success = true;
                    return r;
                }
            }
            r.Error = "未解析到译文";
            return r;
        }
    }

    /// <summary>带缓存与降级链的翻译门面。</summary>
    public class TranslationService
    {
        private readonly List<ITranslator> _chain = new List<ITranslator>();
        private readonly Dictionary<string, TranslationResult> _cache = new Dictionary<string, TranslationResult>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();
        private const int CacheLimit = 400;
        private readonly object _lock = new object();

        public TranslationService()
        {
            _chain.Add(new TencentTranslator());
            _chain.Add(new YoudaoTranslator());
            _chain.Add(new MyMemoryTranslator());
        }

        public TranslationResult Translate(string text, Direction dir)
        {
            string from, to;
            Lang.Resolve(dir, text, out from, out to);
            return Translate(text, from, to);
        }

        public TranslationResult Translate(string text, string from, string to)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return new TranslationResult { Error = "空文本" };

            string key = from + "|" + to + "|" + text;
            lock (_lock)
            {
                TranslationResult hit;
                if (_cache.TryGetValue(key, out hit)) return hit;
            }

            TranslationResult last = null;
            foreach (ITranslator t in _chain)
            {
                try
                {
                    TranslationResult r = t.Translate(text, from, to);
                    if (r != null && r.Success)
                    {
                        lock (_lock) { Remember(key, r); }
                        return r;
                    }
                    last = r;
                }
                catch (Exception ex)
                {
                    last = new TranslationResult { Engine = t.Name, Error = ex.Message };
                }
            }

            return last ?? new TranslationResult { Error = "无可用翻译引擎" };
        }

        private void Remember(string key, TranslationResult r)
        {
            if (_cache.Count >= CacheLimit)
            {
                int drop = Math.Max(1, CacheLimit / 4);
                for (int i = 0; i < drop && i < _order.Count; i++) _cache.Remove(_order[i]);
                _order.RemoveRange(0, Math.Min(drop, _order.Count));
            }
            _cache[key] = r;
            _order.Add(key);
        }
    }
}
