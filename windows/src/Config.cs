using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace HoverTranslate
{
    public class AppConfig
    {
        public int Direction { get; set; }
        public bool HoverEnabled { get; set; }
        public int HoverDelayMs { get; set; }
        public int MaxTextLength { get; set; }
        public bool ShowOriginal { get; set; }
        public bool DarkTheme { get; set; }
        public double FontSize { get; set; }
        public bool AutoStart { get; set; }
        public bool QuietMode { get; set; }
        public bool AutoDim { get; set; }
        public string TargetLang { get; set; }
        public bool SelectionExplain { get; set; }
        public int CardAnchor { get; set; }
        /// <summary>卡片放在光标上方时额外上移的行数，0~4。</summary>
        public int LiftLines { get; set; }
        public string Industry { get; set; }
        public string HotkeyTranslate { get; set; }
        public string HotkeySwitch { get; set; }
        public string HotkeyToggle { get; set; }

        public AppConfig()
        {
            Direction = (int)HoverTranslate.Direction.Auto;
            HoverEnabled = true;
            HoverDelayMs = 220;
            MaxTextLength = 240;
            ShowOriginal = false;
            QuietMode = true;
            AutoDim = true;
            TargetLang = "zh";
            SelectionExplain = true;
            CardAnchor = 0;
            LiftLines = 1;
            Industry = "auto";
            DarkTheme = true;
            FontSize = 15;
            // 装好就跟着开机起来，菜单里点一下可以关掉
            AutoStart = true;
            HotkeyTranslate = "Ctrl+Alt+T";
            HotkeySwitch = "Ctrl+Alt+D";
            HotkeyToggle = "Ctrl+Alt+H";
        }

        private static string _dir;

        /// <summary>配置目录：优先 %APPDATA%\HoverTranslate，不可写时退回程序目录。</summary>
        public static string DirPath
        {
            get
            {
                if (_dir != null) return _dir;

                string candidate = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HoverTranslate");

                if (EnsureWritable(candidate)) { _dir = candidate; return _dir; }

                string fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                if (EnsureWritable(fallback)) { _dir = fallback; return _dir; }

                _dir = AppDomain.CurrentDomain.BaseDirectory;
                return _dir;
            }
        }

        private static bool EnsureWritable(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".w");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        /// <summary>自检用：把配置目录指到临时位置，避免污染用户真实配置。</summary>
        public static void UseDirForTest(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                _dir = dir;
            }
            catch { }
        }

        public static string ConfigPath { get { return Path.Combine(DirPath, "config.json"); } }
        public static string LogPath { get { return Path.Combine(DirPath, "log.txt"); } }

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                    var ser = new JavaScriptSerializer();
                    var cfg = ser.Deserialize<AppConfig>(json);
                    if (cfg != null) return cfg;
                }
            }
            catch (Exception ex) { Log("config load: " + ex.Message); }
            return new AppConfig();
        }

        public void Save()
        {
            try
            {
                var ser = new JavaScriptSerializer();
                File.WriteAllText(ConfigPath, ser.Serialize(this), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log("config save: " + ex.Message); }
        }

        public static void Log(string msg)
        {
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine;
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
            catch { }
        }

        public void Normalize()
        {
            if (HoverDelayMs < 120) HoverDelayMs = 120;
            if (HoverDelayMs > 2000) HoverDelayMs = 2000;
            if (MaxTextLength < 40) MaxTextLength = 40;
            if (MaxTextLength > 2000) MaxTextLength = 2000;
            if (FontSize < 10) FontSize = 10;
            if (FontSize > 28) FontSize = 28;
            if (Direction < 0 || Direction > 2) Direction = 0;
            if (string.IsNullOrEmpty(HotkeyTranslate)) HotkeyTranslate = "Ctrl+Alt+T";
            if (string.IsNullOrEmpty(HotkeySwitch)) HotkeySwitch = "Ctrl+Alt+D";
            if (string.IsNullOrEmpty(HotkeyToggle)) HotkeyToggle = "Ctrl+Alt+H";
            if (string.IsNullOrEmpty(TargetLang)) TargetLang = "zh";
            if (TargetLang != "zh" && TargetLang != "en" && TargetLang != "both") TargetLang = "zh";
            if (CardAnchor < 0 || CardAnchor > 2) CardAnchor = 0;
            if (LiftLines < 0) LiftLines = 0;
            if (LiftLines > 4) LiftLines = 4;
            if (!IndustryTerms.IsKnownAny(Industry)) Industry = "auto";
        }
    }
}
