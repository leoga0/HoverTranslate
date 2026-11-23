using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace HoverTranslate
{
    /// <summary>用户自己加的行业/岗位。</summary>
    public class UserIndustry
    {
        public string key = "";
        public string label = "";
    }

    /// <summary>用户自己加的术语。</summary>
    public class UserTerm
    {
        public string i = "";   // 所属行业 key
        public string n = "";   // 名称
        public string a = "";   // 别名，| 分隔
        public string d = "";   // 一句话解释
        public string x = "";   // 生活里的比方
    }

    /// <summary>
    /// 自定义词库：行业（含岗位）和词条都存在配置目录下的 json 里，
    /// 菜单里能直接加，也可以手工编辑文件。文件坏了不影响内置词库。
    /// </summary>
    public static class UserDict
    {
        public static readonly List<UserIndustry> Industries = new List<UserIndustry>();
        public static readonly List<UserTerm> Terms = new List<UserTerm>();

        private static bool _loaded;

        public static string IndustriesPath { get { return Path.Combine(AppConfig.DirPath, "user-industries.json"); } }
        public static string TermsPath { get { return Path.Combine(AppConfig.DirPath, "user-terms.json"); } }

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            Industries.Clear();
            Terms.Clear();

            try
            {
                var ser = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
                if (File.Exists(IndustriesPath))
                {
                    string json = File.ReadAllText(IndustriesPath, Encoding.UTF8);
                    List<UserIndustry> list = ser.Deserialize<List<UserIndustry>>(json);
                    if (list != null)
                    {
                        foreach (UserIndustry ind in list)
                        {
                            if (ind == null || string.IsNullOrEmpty(ind.key) || string.IsNullOrEmpty(ind.label)) continue;
                            if (FindIndustry(ind.key) != null) continue;
                            Industries.Add(ind);
                        }
                    }
                }
            }
            catch (Exception ex) { AppConfig.Log("user industries load: " + ex.Message); }

            try
            {
                var ser = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
                if (File.Exists(TermsPath))
                {
                    string json = File.ReadAllText(TermsPath, Encoding.UTF8);
                    List<UserTerm> list = ser.Deserialize<List<UserTerm>>(json);
                    if (list != null)
                    {
                        foreach (UserTerm t in list)
                        {
                            if (t == null || string.IsNullOrEmpty(t.n) || string.IsNullOrEmpty(t.d)) continue;
                            Terms.Add(t);
                        }
                    }
                }
            }
            catch (Exception ex) { AppConfig.Log("user terms load: " + ex.Message); }
        }

        public static UserIndustry FindIndustry(string key)
        {
            foreach (UserIndustry ind in Industries)
            {
                if (ind.key == key) return ind;
            }
            return null;
        }

        public static string LabelOf(string key)
        {
            UserIndustry ind = FindIndustry(key);
            if (ind != null) return ind.label;
            return "自定义";
        }

        /// <summary>新增一个行业/岗位，返回它的 key；重名直接返回已有 key。</summary>
        public static string AddIndustry(string label)
        {
            label = (label ?? "").Trim();
            if (label.Length == 0) return null;

            foreach (string[] d in IndustryTerms.Domains)
            {
                if (d[1] == label) return d[0];
            }
            foreach (UserIndustry ind in Industries)
            {
                if (ind.label == label) return ind.key;
            }

            var item = new UserIndustry { key = "u" + (Industries.Count + 1) + "-" + Guid.NewGuid().ToString("N").Substring(0, 4), label = label };
            Industries.Add(item);
            SaveIndustries();
            return item.key;
        }

        public static void AddTerm(UserTerm t)
        {
            if (t == null || string.IsNullOrEmpty(t.n) || string.IsNullOrEmpty(t.d)) return;
            Terms.Add(t);
            SaveTerms();
        }

        public static bool SaveIndustries()
        {
            return Write(IndustriesPath, Industries);
        }

        public static bool SaveTerms()
        {
            return Write(TermsPath, Terms);
        }

        private static bool Write<T>(string path, T data)
        {
            try
            {
                var ser = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
                File.WriteAllText(path, ser.Serialize(data), new UTF8Encoding(false));
                return true;
            }
            catch (Exception ex)
            {
                AppConfig.Log("user dict save: " + ex.Message);
                return false;
            }
        }

        /// <summary>把自定义词条转成匹配引擎用的条目。</summary>
        public static List<Terminology.Term> ToTerms()
        {
            var list = new List<Terminology.Term>();
            foreach (UserTerm t in Terms)
            {
                list.Add(new Terminology.Term
                {
                    n = t.n,
                    a = t.a ?? "",
                    d = t.d,
                    x = t.x ?? "",
                    c = LabelOf(t.i),
                    i = t.i
                });
            }
            return list;
        }
    }
}
