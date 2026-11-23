using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace HoverTranslate
{
    public partial class TranslatorApp : Application
    {
        private const int HOTKEY_TRANSLATE = 9001;
        private const int HOTKEY_SWITCH = 9002;
        private const int HOTKEY_TOGGLE = 9003;

        private AppConfig _cfg;
        private TranslationService _service;
        private CardWindow _card;
        private Forms.NotifyIcon _tray;
        private Forms.ContextMenuStrip _menu;
        private Forms.ToolStripMenuItem _miHover;
        private Forms.ToolStripMenuItem _miTargetZh;
        private Forms.ToolStripMenuItem _miTargetEn;
        private Forms.ToolStripMenuItem _miTargetBoth;
        private Forms.ToolStripMenuItem _miAnchorMouse;
        private Forms.ToolStripMenuItem _miAnchorRight;
        private Forms.ToolStripMenuItem _miAnchorCorner;
        private Forms.ToolStripMenuItem _miDark;
        private Forms.ToolStripMenuItem _miShowSrc;
        private Forms.ToolStripMenuItem _miQuiet;
        private Forms.ToolStripMenuItem _miAutoDim;
        private Forms.ToolStripMenuItem _miSel;
        private Forms.ToolStripMenuItem _miFontBig;
        private Forms.ToolStripMenuItem _miFontMid;
        private Forms.ToolStripMenuItem _miFontSmall;
        private Forms.ToolStripMenuItem _miAutoStart;
        private Forms.ToolStripMenuItem _indMenu;
        private readonly System.Collections.Generic.Dictionary<int, Forms.ToolStripMenuItem> _miLift =
            new System.Collections.Generic.Dictionary<int, Forms.ToolStripMenuItem>();
        private readonly System.Collections.Generic.Dictionary<string, Forms.ToolStripMenuItem> _miIndustry =
            new System.Collections.Generic.Dictionary<string, Forms.ToolStripMenuItem>(StringComparer.Ordinal);

        /// <summary>自检模式：不弹卡片、不建托盘图标，只跑开关逻辑。</summary>
        internal bool Headless;

        private DispatcherTimer _timer;
        private HwndSource _hotkeyHost;

        private POINT _lastPos = new POINT(-1, -1);
        private DateTime _stillSince = DateTime.MinValue;
        private POINT _lastTrigger = new POINT(-99999, -99999);
        private bool _busy;
        private int _requestId;
        private DispatcherTimer _selTimer;
        private string _lastSelection;
        private bool _selProbeBusy;
        private bool _explainingSelection;
        private DateTime _suppressHoverUntil = DateTime.MinValue;
        private DateTime _cardShownAt = DateTime.MinValue;

        [STAThread]
        public static void Main(string[] args)
        {
            bool allowMultiple = false;
            if (args != null)
            {
                foreach (string a in args)
                {
                    if (string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase))
                    {
                        TranslatorApp.SelfTestMain();
                        return;
                    }
                    if (string.Equals(a, "--allow-multiple", StringComparison.OrdinalIgnoreCase))
                    {
                        allowMultiple = true;
                    }
                }
            }

            try
            {
                bool created;
                using (var mutex = new Mutex(true, "HoverTranslate.SingleInstance.Mutex", out created))
                {
                    if (!created && !allowMultiple) return;
                    Native.EnablePerMonitorDpi();
                    var app = new TranslatorApp();
                    app.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                    {
                        AppConfig.Log("UI EX: " + e.Exception.GetType().Name + " " + e.Exception.Message + "\n" + e.Exception.StackTrace);
                        e.Handled = true;
                    };
                    AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
                    {
                        var ex = e.ExceptionObject as Exception;
                        AppConfig.Log("APP EX: " + (ex == null ? "unknown" : ex.GetType().Name + " " + ex.Message + "\n" + ex.StackTrace));
                    };
                    app.Run();
                }
            }
            catch (Exception ex)
            {
                AppConfig.Log("MAIN EX: " + ex.GetType().Name + " " + ex.Message + "\n" + ex.StackTrace);
            }
        }
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            AppConfig.Log("OnStartup begin");
            UserDict.Load();
            _cfg = AppConfig.Load();
            _cfg.Normalize();
            Terminology.Domain = _cfg.Industry;
            _service = new TranslationService();

            _card = new CardWindow();
            _card.QuietMode = _cfg.QuietMode;
            _card.CardAnchor = _cfg.CardAnchor;
            _card.AutoDimEnabled = _cfg.AutoDim;
            _card.LiftLines = _cfg.LiftLines;
            _card.ApplyTheme(_cfg.DarkTheme, _cfg.FontSize);
            AppConfig.Log("card ok");

            try { SetupTray(); AppConfig.Log("tray ok"); }
            catch (Exception ex) { AppConfig.Log("tray fail: " + ex.Message + "\n" + ex.StackTrace); }

            try { SetupHotkeys(); AppConfig.Log("hotkeys ok"); }
            catch (Exception ex) { AppConfig.Log("hotkeys fail: " + ex.Message + "\n" + ex.StackTrace); }

            _selTimer = new DispatcherTimer(DispatcherPriority.Background);
            _selTimer.Interval = TimeSpan.FromMilliseconds(450);
            _selTimer.Tick += OnSelectionSettled;

            _timer = new DispatcherTimer(DispatcherPriority.Background);
            _timer.Interval = TimeSpan.FromMilliseconds(110);
            _timer.Tick += OnTick;
            _timer.Start();

            AppConfig.Log("started dir=" + _cfg.Direction + " hover=" + _cfg.HoverEnabled + " delay=" + _cfg.HoverDelayMs);

        }

        // ---------------- 托盘 ----------------

        private void SetupTray()
        {
            _tray = new Forms.NotifyIcon();
            _tray.Icon = Native.BuildTrayIcon(_cfg.DarkTheme);
            _tray.Text = "悬停翻译 · 鼠标停在文字上即出译文";
            _tray.Visible = !Headless;

            _menu = new Forms.ContextMenuStrip();
            _menu.ShowImageMargin = false;
            // 展开菜单前先按配置刷新勾选，并把卡片收掉：卡片是置顶窗口，
            // 停在托盘附近时会挡住弹出菜单，点上去像是开关失灵。
            _menu.Opening += delegate
            {
                try { _card.HideCard(true); } catch { }
                SyncMenu();
            };

            _miHover = new Forms.ToolStripMenuItem("悬停翻译", null, delegate { ToggleHover(); });

            var targetMenu = new Forms.ToolStripMenuItem("翻译目标");
            _miTargetZh = new Forms.ToolStripMenuItem("译成中文（只在非中文上翻译）", null, delegate { SetTargetLang("zh"); });
            _miTargetEn = new Forms.ToolStripMenuItem("译成英文（只在中文上翻译）", null, delegate { SetTargetLang("en"); });
            _miTargetBoth = new Forms.ToolStripMenuItem("双向都翻", null, delegate { SetTargetLang("both"); });
            targetMenu.DropDownItems.Add(_miTargetZh);
            targetMenu.DropDownItems.Add(_miTargetEn);
            targetMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            targetMenu.DropDownItems.Add(_miTargetBoth);

            var indMenu = new Forms.ToolStripMenuItem("行业词库");
            _indMenu = indMenu;
            BuildIndustryMenu();

            var posMenu = new Forms.ToolStripMenuItem("卡片位置");
            _miAnchorMouse = new Forms.ToolStripMenuItem("鼠标左上方（不挡下文）", null, delegate { SetAnchor(0); });
            _miAnchorRight = new Forms.ToolStripMenuItem("鼠标右侧", null, delegate { SetAnchor(1); });
            _miAnchorCorner = new Forms.ToolStripMenuItem("固定右下角", null, delegate { SetAnchor(2); });
            posMenu.DropDownItems.Add(_miAnchorMouse);
            posMenu.DropDownItems.Add(_miAnchorRight);
            posMenu.DropDownItems.Add(_miAnchorCorner);
            posMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            foreach (int lines in new int[] { 0, 1, 2 })
            {
                int n = lines;
                var item = new Forms.ToolStripMenuItem("上方多让 " + n + " 行", null, delegate { SetLift(n); });
                _miLift[n] = item;
                posMenu.DropDownItems.Add(item);
            }

            var themeMenu = new Forms.ToolStripMenuItem("外观");
            _miDark = new Forms.ToolStripMenuItem("深色卡片", null, delegate { ToggleTheme(); });
            _miFontBig = new Forms.ToolStripMenuItem("大字号", null, delegate { SetFont(17); });
            _miFontMid = new Forms.ToolStripMenuItem("中字号", null, delegate { SetFont(15); });
            _miFontSmall = new Forms.ToolStripMenuItem("小字号", null, delegate { SetFont(13); });
            _miShowSrc = new Forms.ToolStripMenuItem("显示原文", null, delegate { ToggleSource(); });
            _miQuiet = new Forms.ToolStripMenuItem("低调样式", null, delegate { ToggleQuiet(); });
            _miAutoDim = new Forms.ToolStripMenuItem("闲置自动变淡", null, delegate { ToggleAutoDim(); });
            _miSel = new Forms.ToolStripMenuItem("选中即解释", null, delegate { ToggleSelectionExplain(); });
            themeMenu.DropDownItems.Add(_miDark);
            themeMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            themeMenu.DropDownItems.Add(_miFontBig);
            themeMenu.DropDownItems.Add(_miFontMid);
            themeMenu.DropDownItems.Add(_miFontSmall);
            themeMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            themeMenu.DropDownItems.Add(_miShowSrc);
            themeMenu.DropDownItems.Add(_miQuiet);
            themeMenu.DropDownItems.Add(_miAutoDim);
            themeMenu.DropDownItems.Add(_miSel);

            _miAutoStart = new Forms.ToolStripMenuItem("开机自动启动", null, delegate { ToggleAutoStart(); });

            var miOpenCfg = new Forms.ToolStripMenuItem("打开配置文件", null, delegate
            {
                try { Process.Start("explorer.exe", "/select,\"" + AppConfig.ConfigPath + "\""); } catch { }
            });

            var miHelp = new Forms.ToolStripMenuItem("快捷键说明", null, delegate
            {
                MessageBox.Show(
                    "鼠标停在需要翻译的文字上约 " + _cfg.HoverDelayMs + " 毫秒，自动弹出译文。\n\n" +
                    "Ctrl+Alt+T   立即翻译光标处的文字（取不到则翻剪贴板）\n" +
                    "Ctrl+Alt+D   切换翻译目标（中文 / 英文 / 双向）\n" +
                    "Ctrl+Alt+H   暂停或恢复悬停翻译\n\n" +
                    "「翻译目标」决定什么情况才翻译：译成中文时，鼠标停在中文上不会弹卡片。\n" +
                    "一句话里中英混排时，只翻非目标语言的部分。\n\n" +
                    "「行业词库」选中自己那一行后，该行业的行话优先解释，选中文字也能用。\n\n" +
                    "卡片：按住左键可拖动，右键复制译文，右上角 × 关闭。",
                    "悬停翻译",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });

            var miExit = new Forms.ToolStripMenuItem("退出", null, delegate { ExitApp(); });

            _menu.Items.Add(_miHover);
            _menu.Items.Add(new Forms.ToolStripSeparator());
            _menu.Items.Add(targetMenu);
            _menu.Items.Add(indMenu);
            _menu.Items.Add(posMenu);
            _menu.Items.Add(themeMenu);
            _menu.Items.Add(_miAutoStart);
            _menu.Items.Add(new Forms.ToolStripSeparator());
            _menu.Items.Add(miOpenCfg);
            _menu.Items.Add(miHelp);
            _menu.Items.Add(new Forms.ToolStripSeparator());
            _menu.Items.Add(miExit);

            _tray.ContextMenuStrip = _menu;
            _tray.DoubleClick += delegate { ToggleHover(); };
            SyncMenu();
        }

        /// <summary>
        /// 菜单勾选状态的唯一出口：每次改过配置都调它，菜单展开时也调一遍。
        /// 以前有几个开关只在创建时设了一次 Checked，点完看不到状态变化，像是没生效。
        /// </summary>
        private void SyncMenu()
        {
            if (_cfg == null) return;

            if (_miHover != null) _miHover.Checked = _cfg.HoverEnabled;
            if (_miDark != null) _miDark.Checked = _cfg.DarkTheme;
            if (_miShowSrc != null) _miShowSrc.Checked = _cfg.ShowOriginal;
            if (_miQuiet != null) _miQuiet.Checked = _cfg.QuietMode;
            if (_miAutoDim != null) _miAutoDim.Checked = _cfg.AutoDim;
            if (_miSel != null) _miSel.Checked = _cfg.SelectionExplain;
            if (_miAutoStart != null) _miAutoStart.Checked = IsAutoStartEnabled();

            if (_miTargetZh != null)
            {
                _miTargetZh.Checked = _cfg.TargetLang == "zh";
                _miTargetEn.Checked = _cfg.TargetLang == "en";
                _miTargetBoth.Checked = _cfg.TargetLang == "both";
            }

            if (_miAnchorMouse != null)
            {
                _miAnchorMouse.Checked = _cfg.CardAnchor == 0;
                _miAnchorRight.Checked = _cfg.CardAnchor == 1;
                _miAnchorCorner.Checked = _cfg.CardAnchor == 2;
            }

            if (_miFontBig != null)
            {
                _miFontBig.Checked = Math.Abs(_cfg.FontSize - 17) < 0.5;
                _miFontMid.Checked = Math.Abs(_cfg.FontSize - 15) < 0.5;
                _miFontSmall.Checked = Math.Abs(_cfg.FontSize - 13) < 0.5;
            }

            foreach (var pair in _miLift)
            {
                pair.Value.Checked = pair.Key == _cfg.LiftLines;
            }

            foreach (var pair in _miIndustry)
            {
                pair.Value.Checked = pair.Key == _cfg.Industry;
            }
        }

        private void SetTargetLang(string lang)
        {
            _cfg.TargetLang = lang;
            _cfg.Save();
            SyncMenu();
            AppConfig.Log("menu target -> " + lang);
            ShowToast(lang == "zh" ? "译成中文 · 中文不再触发" : (lang == "en" ? "译成英文 · 英文不再触发" : "双向都翻"));
        }

        private void SetIndustry(string key)
        {
            if (!IndustryTerms.IsKnownAny(key)) key = "auto";
            _cfg.Industry = key;
            _cfg.Save();
            Terminology.Domain = key;
            SyncMenu();
            AppConfig.Log("menu industry -> " + key + " (词库共 " + Terminology.TotalCount + " 条)");
            ShowToast("行业词库：" + IndustryTerms.LabelAny(key));
        }

        /// <summary>重建「行业词库」子菜单：内置行业 + 用户自己加的行业/岗位 + 管理入口。</summary>
        private void BuildIndustryMenu()
        {
            if (_indMenu == null) return;

            _indMenu.DropDownItems.Clear();
            _miIndustry.Clear();

            foreach (string[] domain in IndustryTerms.AllDomains())
            {
                string key = domain[0];
                var item = new Forms.ToolStripMenuItem(domain[1], null, delegate { SetIndustry(key); });
                _miIndustry[key] = item;
                _indMenu.DropDownItems.Add(item);
                if (key == "auto") _indMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            }

            _indMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            _indMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("新增行业 / 岗位…", null, delegate { AddIndustryInteractive(); }));
            _indMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("给当前行业加词条…", null, delegate { AddTermInteractive(); }));
            _indMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("打开自定义词库文件", null, delegate { OpenUserDict(); }));
            SyncMenu();
        }

        private void AddIndustryInteractive()
        {
            string[] r = Prompt.Ask("新增行业 / 岗位",
                new string[] { "名称（行业或岗位，如：粮食加工、护士、育儿嫂）" },
                new string[] { "" },
                "加完之后它会出现在「行业词库」里，选中它就能继续给它加词条。");
            if (r == null) return;

            string label = (r[0] ?? "").Trim();
            string key = UserDict.AddIndustry(label);
            if (string.IsNullOrEmpty(key)) { ShowToast("名称没填，未添加"); return; }

            Terminology.Invalidate();
            BuildIndustryMenu();
            SetIndustry(key);
            AppConfig.Log("industry add -> " + key + " label=" + label);
        }

        private void AddTermInteractive()
        {
            if (string.IsNullOrEmpty(_cfg.Industry) || _cfg.Industry == "auto")
            {
                ShowToast("先在「行业词库」里选一个行业，再加词条");
                return;
            }

            string label = IndustryTerms.LabelAny(_cfg.Industry);
            string[] r = Prompt.Ask("添加词条 · " + label,
                new string[]
                {
                    "术语 / 黑话 / 专有名词",
                    "别名（可空，多个用 | 分隔）",
                    "一句话解释（写给不懂这行的人）",
                    "生活里的比方（可空）"
                },
                new string[] { "", "", "", "" },
                "解释按「一句话说清是什么 + 打个比方」写，不要用术语解释术语。");
            if (r == null) return;

            string name = (r[0] ?? "").Trim();
            string def = (r[2] ?? "").Trim();
            if (name.Length < 2) { ShowToast("术语名称太短，至少 2 个字"); return; }
            if (def.Length == 0) { ShowToast("还没写解释，词条没有保存"); return; }

            UserDict.AddTerm(new UserTerm
            {
                i = _cfg.Industry,
                n = name,
                a = (r[1] ?? "").Trim(),
                d = def,
                x = (r[3] ?? "").Trim()
            });
            Terminology.Invalidate();
            AppConfig.Log("term add -> " + name + " @" + _cfg.Industry + "，词库合计 " + Terminology.TotalCount + " 条");
            SetIndustry(_cfg.Industry);
            ShowToast("已加入「" + label + "」：" + name);
        }

        private void OpenUserDict()
        {
            try
            {
                string path = File.Exists(UserDict.TermsPath) ? UserDict.TermsPath : AppConfig.ConfigPath;
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
            }
            catch { }
        }

        private void SetAnchor(int mode)
        {
            _cfg.CardAnchor = mode;
            _cfg.Save();
            _card.CardAnchor = mode;
            SyncMenu();
            AppConfig.Log("menu anchor -> " + mode);
            ShowToast(mode == 0 ? "卡片在鼠标左上方" : (mode == 1 ? "卡片在鼠标右侧" : "卡片固定在右下角"));
        }

        private void SetLift(int lines)
        {
            _cfg.LiftLines = lines < 0 ? 0 : (lines > 4 ? 4 : lines);
            _cfg.Save();
            _card.LiftLines = _cfg.LiftLines;
            SyncMenu();
            AppConfig.Log("menu liftLines -> " + _cfg.LiftLines);
            ShowToast(_cfg.LiftLines == 0 ? "卡片紧贴光标上方" : "卡片上方多让 " + _cfg.LiftLines + " 行");
        }

        private void ToggleHover()
        {
            _cfg.HoverEnabled = !_cfg.HoverEnabled;
            _cfg.Save();
            SyncMenu();
            AppConfig.Log("menu hover -> " + _cfg.HoverEnabled);
            if (!_cfg.HoverEnabled) _card.HideCard(true);
            ShowToast(_cfg.HoverEnabled ? "悬停翻译已开启" : "悬停翻译已暂停");
        }

        private void ToggleTheme()
        {
            _cfg.DarkTheme = !_cfg.DarkTheme;
            _cfg.Save();
            RefreshCardStyle();
            SyncMenu();
            AppConfig.Log("menu theme dark -> " + _cfg.DarkTheme);
            ShowToast(_cfg.DarkTheme ? "深色卡片" : "浅色卡片");
        }

        private void ToggleSource()
        {
            _cfg.ShowOriginal = !_cfg.ShowOriginal;
            _cfg.Save();
            SyncMenu();
            AppConfig.Log("menu showSource -> " + _cfg.ShowOriginal);
            ShowToast(_cfg.ShowOriginal ? "卡片显示原文" : "卡片隐藏原文");
        }

        private void ToggleQuiet()
        {
            _cfg.QuietMode = !_cfg.QuietMode;
            _cfg.Save();
            RefreshCardStyle();
            SyncMenu();
            AppConfig.Log("menu quiet -> " + _cfg.QuietMode);
            ShowToast(_cfg.QuietMode ? "低调样式 · 更透" : "标准样式 · 更实");
        }

        private void ToggleSelectionExplain()
        {
            _cfg.SelectionExplain = !_cfg.SelectionExplain;
            _cfg.Save();
            SyncMenu();
            if (!_cfg.SelectionExplain)
            {
                // 关掉就把排队中的解释也取消，否则还会弹最后一次
                _selTimer.Stop();
                _lastSelection = null;
            }
            AppConfig.Log("menu selectionExplain -> " + _cfg.SelectionExplain);
            ShowToast(_cfg.SelectionExplain ? "选中文字就会解释" : "已关闭选中即解释");
        }

        private void ToggleAutoDim()
        {
            _cfg.AutoDim = !_cfg.AutoDim;
            _cfg.Save();
            _card.AutoDimEnabled = _cfg.AutoDim;
            SyncMenu();
            if (!_cfg.AutoDim)
            {
                // 关掉时把已经压暗的卡片恢复，不然看上去像没生效
                _card.SetDim(false);
            }
            else
            {
                ScheduleAutoDim();
            }
            AppConfig.Log("menu autoDim -> " + _cfg.AutoDim);
            ShowToast(_cfg.AutoDim ? "闲置自动变淡" : "常亮显示");
        }

        private void RefreshCardStyle()
        {
            _card.QuietMode = _cfg.QuietMode;
            _card.CardAnchor = _cfg.CardAnchor;
            _card.AutoDimEnabled = _cfg.AutoDim;
            _card.LiftLines = _cfg.LiftLines;
            _card.ApplyTheme(_cfg.DarkTheme, _cfg.FontSize);
        }

        private void SetFont(double size)
        {
            _cfg.FontSize = size;
            _cfg.Save();
            RefreshCardStyle();
            SyncMenu();
            AppConfig.Log("menu fontSize -> " + size);
            ShowToast("字号 " + (int)size);
        }

        /// <summary>开关操作的可见反馈。位置跟着当前鼠标走，不然会出现在上一次取词的地方。</summary>
        private void ShowToast(string text)
        {
            AppConfig.Log("toast: " + text);
            if (Headless) return;
            try
            {
                POINT p = Native.CursorPos();
                _card.ShowMessageAt(text, "", p.X, p.Y);
                if (_card.IsVisible)
                {
                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
                    t.Tick += delegate { t.Stop(); _card.HideCard(false); };
                    t.Start();
                }
            }
            catch (Exception ex) { AppConfig.Log("toast fail: " + ex.Message); }
        }

        // ---------------- 开机自启 ----------------

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "HoverTranslate";

        private static bool IsAutoStartEnabled()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    object v = k.GetValue(RunName);
                    return v != null;
                }
            }
            catch { return false; }
        }

        private void ToggleAutoStart()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (IsAutoStartEnabled())
                    {
                        k.DeleteValue(RunName, false);
                        _miAutoStart.Checked = false;
                        ShowToast("已关闭开机自启");
                    }
                    else
                    {
                        string exe = Process.GetCurrentProcess().MainModule.FileName;
                        k.SetValue(RunName, "\"" + exe + "\"");
                        _miAutoStart.Checked = true;
                        ShowToast("已开启开机自启");
                    }
                }
            }
            catch (Exception ex) { AppConfig.Log("autostart: " + ex.Message); }
        }

        // ---------------- 热键 ----------------

        private void SetupHotkeys()
        {
            var p = new HwndSourceParameters("HoverTranslateHotkeyHost");
            p.Width = 0;
            p.Height = 0;
            p.WindowStyle = 0;
            p.ExtendedWindowStyle = 0;
            p.ParentWindow = IntPtr.Zero;

            _hotkeyHost = new HwndSource(p);
            _hotkeyHost.AddHook(WndProc);

            IntPtr h = _hotkeyHost.Handle;
            Native.RegisterHotKey(h, HOTKEY_TRANSLATE, Native.MOD_CONTROL | Native.MOD_ALT, 0x54); // T
            Native.RegisterHotKey(h, HOTKEY_SWITCH, Native.MOD_CONTROL | Native.MOD_ALT, 0x44);    // D
            Native.RegisterHotKey(h, HOTKEY_TOGGLE, Native.MOD_CONTROL | Native.MOD_ALT, 0x48);    // H
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HOTKEY_TRANSLATE) { handled = true; TranslateNow(); }
                else if (id == HOTKEY_SWITCH) { handled = true; CycleDirection(); }
                else if (id == HOTKEY_TOGGLE) { handled = true; ToggleHover(); }
            }
            return IntPtr.Zero;
        }

        private void CycleDirection()
        {
            string[] order = { "zh", "en", "both" };
            int idx = Array.IndexOf(order, _cfg.TargetLang);
            if (idx < 0) idx = 0;
            SetTargetLang(order[(idx + 1) % order.Length]);
        }

        private void TranslateNow()
        {
            POINT p = Native.CursorPos();
            Trigger(p, true);
        }

        // ---------------- 悬停主循环 ----------------

        private int _tickCount;

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                _tickCount++;

                // 每 4 个 tick（约 440ms）探一次选区，放后台线程，别卡 UI
                if (_cfg.SelectionExplain && _tickCount % 4 == 0 && !_selProbeBusy)
                {
                    _selProbeBusy = true;
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        string sel = null;
                        try { sel = TextGrabber.GetSelectedText(); }
                        catch { }
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            _selProbeBusy = false;
                            HandleSelectionProbe(sel);
                        }));
                    });
                }

                POINT p = Native.CursorPos();

                if (p.X != _lastPos.X || p.Y != _lastPos.Y)
                {
                    _lastPos = p;
                    _stillSince = DateTime.UtcNow;
                    AutoDismissIfFar(p);
                    return;
                }


                if (!_cfg.HoverEnabled) return;
                if (_busy) return;
                if (_explainingSelection) return;
                if (DateTime.UtcNow < _suppressHoverUntil) return;
                if (_card.IsVisible && _card.ContainsCursor(p.X, p.Y)) return;

                double still = (DateTime.UtcNow - _stillSince).TotalMilliseconds;
                if (still < _cfg.HoverDelayMs) return;

                long dx = (long)p.X - _lastTrigger.X;
                long dy = (long)p.Y - _lastTrigger.Y;
                if (dx * dx + dy * dy < 16) return;

                Trigger(p, false);
            }
            catch (Exception ex)
            {
                AppConfig.Log("tick: " + ex.Message);
            }
        }

        /// <summary>鼠标离开原取词位置较远时，卡片自动淡出。</summary>
        /// <summary>窗口标题和软件名不是待翻译内容。</summary>
        private static bool IsWindowTitle(string text, string winTitle)
        {
            if (string.IsNullOrEmpty(text)) return true;
            if (string.IsNullOrEmpty(winTitle)) return false;
            string a = text.Trim();
            string b = winTitle.Trim();
            if (a.Length == 0 || b.Length == 0) return false;
            if (a == b) return true;
            if (b.StartsWith(a, StringComparison.Ordinal) && a.Length > b.Length - 8) return true;
            return false;
        }

        private void AutoDismissIfFar(POINT p)
        {
            if (!_card.IsVisible) return;
            if ((DateTime.UtcNow - _cardShownAt).TotalMilliseconds < 1200) return;
            if (_card.ContainsCursor(p.X, p.Y)) return;

            long dx = (long)p.X - _lastTrigger.X;
            long dy = (long)p.Y - _lastTrigger.Y;
            if (dx * dx + dy * dy <= 90L * 90L) return;

            _card.HideCard(false);
        }

        private void Trigger(POINT p, bool manual)
        {

            _busy = true;
            _lastTrigger = p;
            int req = ++_requestId;
            int maxLen = _cfg.MaxTextLength;

            ThreadPool.QueueUserWorkItem(delegate
            {
                GrabResult g = null;
                string winTitle = Native.WindowTitleAt(p.X, p.Y);

                // 浏览器等应用的可访问性树是惰性构建的：首次询问常返回容器或失败，
                // 短暂重试一两次即可拿到真实文本节点。
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try { g = TextGrabber.Grab(p.X, p.Y, maxLen); }
                    catch (Exception ex) { AppConfig.Log("grab thread: " + ex.Message); }

                    if (g != null && g.Found && TextShape.Worth(g.Text) && !IsWindowTitle(g.Text, winTitle)) break;
                    Thread.Sleep(170);
                }

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _busy = false;
                    if (req != _requestId) return;
                    HandleGrab(g, p, manual, winTitle);
                }));
            });
        }

        private void HandleGrab(GrabResult g, POINT p, bool manual, string winTitle)
        {
            string text = null;

            if (g != null && g.Found && TextShape.Worth(g.Text) && !IsWindowTitle(g.Text, winTitle))
            {
                text = g.Text;
            }
            else if (manual)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string cb = Clipboard.GetText();
                        if (TextShape.Worth(cb)) text = TextShape.Clean(cb, _cfg.MaxTextLength);
                    }
                }
                catch { }
            }

            if (string.IsNullOrEmpty(text))
            {

                if (manual)
                {
                    _card.ShowMessage("光标下没有取到文字", "把鼠标放到文字上，或先选中文字再按快捷键");
                    AutoHide(1400);
                }
                return;
            }

            if (_card.IsVisible && _card.Tag as string == text) return;
            _card.Tag = text;
            // 记下取词所在的文本块，卡片摆位时避开
            if (g != null && g.Found) _card.SetAvoidRect(g.Rect);
            else _card.SetAvoidRect(new RECT());

            // 目标语言过滤：整段已经是目标语言时不打扰，直接不弹，顺手把旧卡片收掉
            if (!ShouldTranslateText(text, manual))
            {
                if (!manual) _card.HideCard(false);
                return;
            }

            RunTranslate(text, p, false);
        }

        /// <summary>统一的取词/选区展示流程：先占位，再翻译，最后补术语。</summary>
        private void RunTranslate(string text, POINT p, bool useAi, bool explain = false)
        {
            _card.CardAnchor = _cfg.CardAnchor;
            _cardShownAt = DateTime.UtcNow;
            _card.ShowLoading(text, TargetLabel(), p.X, p.Y, _cfg.ShowOriginal);

            int req = _requestId;
            string target = _cfg.TargetLang;

            ThreadPool.QueueUserWorkItem(delegate
            {
                MixedOutcome m = null;
                try { m = _service.TranslateForTarget(text, target, explain); }
                catch (Exception ex) { m = new MixedOutcome { Error = ex.Message }; }

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (req != _requestId) return;

                    if (m == null || !m.ShouldShow)
                    {
                        _card.HideCard(true);
                        return;
                    }

                    bool explainOnly = m.ExplainOnly;
                    var r = new TranslationResult
                    {
                        Success = explainOnly || m.Success,
                        Source = m.Source,
                        Target = explainOnly ? "" : m.Target,
                        From = m.From,
                        To = m.To,
                        Engine = explainOnly ? "解释" : m.Engine,
                        Error = m.Error
                    };
                    // 解释模式下原文就是主角，不用等「显示原文」开关
                    _card.ShowResult(r, _cfg.ShowOriginal || explain);
                    _card.ShowTerms(m.Terms);
                    if (explainOnly && m.Terms.Count == 0)
                    {
                        _card.ShowFooterNote(AiExplainer.Available
                            ? "本地词库没命中这些词，正在问模型…"
                            : "本地词库没命中，选中 2~80 个字符更容易解释");
                    }
                    ScheduleAutoDim();
                    if (!_cfg.HoverEnabled) AutoHide(2200);

                    // 本地术语库覆盖不到的，联网让模型用大白话讲一遍
                    if (useAi && m.Terms.Count < 2 && AiExplainer.Available)
                    {
                        string probe = text;
                        ThreadPool.QueueUserWorkItem(delegate
                        {
                            List<Terminology.Term> ai = null;
                            try { ai = AiExplainer.Explain(probe, 2 - m.Terms.Count); }
                            catch { }
                            if (ai == null || ai.Count == 0) return;

                            Dispatcher.BeginInvoke(new Action(delegate
                            {
                                if (req != _requestId) return;
                                var merged = new List<Terminology.Term>(m.Terms);
                                merged.AddRange(ai);
                                _card.ShowTerms(merged);
                            }));
                        });
                    }
                }));
            });
        }

        // ---------- 选中即解释 ----------

        private void HandleSelectionProbe(string sel)
        {
            if (sel == _lastSelection) return;
            _lastSelection = sel;

            if (string.IsNullOrEmpty(sel) || sel.Length < 2 || sel.Length > 80)
            {
                _selTimer.Stop();
                return;
            }

            _selTimer.Stop();
            _selTimer.Start();
        }

        private void OnSelectionSettled(object sender, EventArgs e)
        {
            _selTimer.Stop();
            string text = _lastSelection;
            if (string.IsNullOrEmpty(text) || text.Length < 2) return;
            if (_card.IsVisible && _card.Tag as string == text) return;
            ExplainSelection(text);
        }

        /// <summary>用户主动选中一段内容，就直接解释，不再走语言门槛。</summary>
        private void ExplainSelection(string text)
        {
            _explainingSelection = true;
            _suppressHoverUntil = DateTime.UtcNow.AddSeconds(2.5);

            POINT p = Native.CursorPos();
            // 选区解释没有单行矩形，卡片按鼠标位置摆就行
            _card.SetAvoidRect(new RECT());
            // 锚点跟着本次选区走，否则鼠标一移就被当成「离开取词点」收卡
            _lastTrigger = p;
            _cardShownAt = DateTime.UtcNow;
            _card.Tag = text;
            _requestId++;
            RunTranslate(text, p, true, true);

            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(260) };
            t.Tick += delegate
            {
                t.Stop();
                _explainingSelection = false;
            };
            t.Start();
        }

        /// <summary>结果稳定后把卡片压到背景，避免一直抢注意力；鼠标移上去会恢复。</summary>
        private void ScheduleAutoDim()
        {
            if (!_cfg.AutoDim) return;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2100) };
            t.Tick += delegate
            {
                t.Stop();
                if (_card.IsVisible && _card.HasResult && !_card.IsMouseOver)
                {
                    _card.SetDim(true);
                }
            };
            t.Start();
        }

        /// <summary>整段都已经是目标语言时不翻译；手动快捷键不受限制。</summary>
        private bool ShouldTranslateText(string text, bool manual)
        {
            if (manual) return true;
            // 命中技术术语就值得弹，哪怕整段都是中文、哪怕不需要翻译
            if (Terminology.Match(text, 1).Count > 0) return true;
            if (_cfg.TargetLang == "both") return true;
            bool targetIsCjk = _cfg.TargetLang == "zh";
            return LangSplitter.CountTranslatable(text, targetIsCjk) > 0;
        }

        private string TargetLabel()
        {
            if (_cfg.TargetLang == "both") return "双向";
            return _cfg.TargetLang == "zh" ? "译成中文" : "译成英文";
        }

        private void AutoHide(int ms)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (s, e) => { t.Stop(); _card.HideCard(false); };
            t.Start();
        }

        // ---------------- 退出 ----------------

        private void ExitApp()
        {
            try
            {
                IntPtr h = _hotkeyHost == null ? IntPtr.Zero : _hotkeyHost.Handle;
                if (h != IntPtr.Zero)
                {
                    Native.UnregisterHotKey(h, HOTKEY_TRANSLATE);
                    Native.UnregisterHotKey(h, HOTKEY_SWITCH);
                    Native.UnregisterHotKey(h, HOTKEY_TOGGLE);
                }
            }
            catch { }

            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
            _cfg.Save();
            AppConfig.Log("退出");
            Shutdown();
        }
    }
}
