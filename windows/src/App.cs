using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace HoverTranslate
{
    public class TranslatorApp : Application
    {
        private const int HOTKEY_TRANSLATE = 9001;
        private const int HOTKEY_SWITCH = 9002;
        private const int HOTKEY_TOGGLE = 9003;

        private AppConfig _cfg;
        private TranslationService _service;
        private CardWindow _card;
        private Forms.NotifyIcon _tray;
        private Forms.ToolStripMenuItem _miHover;
        private Forms.ToolStripMenuItem _miAuto;
        private Forms.ToolStripMenuItem _miEnZh;
        private Forms.ToolStripMenuItem _miZhEn;
        private Forms.ToolStripMenuItem _miDark;
        private Forms.ToolStripMenuItem _miAutoStart;
        private DispatcherTimer _timer;
        private HwndSource _hotkeyHost;

        private POINT _lastPos = new POINT(-1, -1);
        private DateTime _stillSince = DateTime.MinValue;
        private POINT _lastTrigger = new POINT(-99999, -99999);
        private bool _busy;
        private int _requestId;

        [STAThread]
        public static void Main()
        {
            try
            {
                bool created;
                using (var mutex = new Mutex(true, "HoverTranslate.SingleInstance.Mutex", out created))
                {
                    if (!created) return;
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
            _cfg = AppConfig.Load();
            _cfg.Normalize();
            _service = new TranslationService();

            _card = new CardWindow();
            _card.QuietMode = _cfg.QuietMode;
            _card.ApplyTheme(_cfg.DarkTheme, _cfg.FontSize);
            AppConfig.Log("card ok");

            try { SetupTray(); AppConfig.Log("tray ok"); }
            catch (Exception ex) { AppConfig.Log("tray fail: " + ex.Message + "\n" + ex.StackTrace); }

            try { SetupHotkeys(); AppConfig.Log("hotkeys ok"); }
            catch (Exception ex) { AppConfig.Log("hotkeys fail: " + ex.Message + "\n" + ex.StackTrace); }

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
            _tray.Visible = true;

            var menu = new Forms.ContextMenuStrip();
            menu.ShowImageMargin = false;

            _miHover = new Forms.ToolStripMenuItem("悬停翻译", null, (s, e) => ToggleHover());
            _miHover.Checked = _cfg.HoverEnabled;

            var dirMenu = new Forms.ToolStripMenuItem("翻译方向");
            _miAuto = new Forms.ToolStripMenuItem("自动识别", null, (s, e) => SetDirection(Direction.Auto));
            _miEnZh = new Forms.ToolStripMenuItem("英译中", null, (s, e) => SetDirection(Direction.EnToZh));
            _miZhEn = new Forms.ToolStripMenuItem("中译英", null, (s, e) => SetDirection(Direction.ZhToEn));
            dirMenu.DropDownItems.Add(_miAuto);
            dirMenu.DropDownItems.Add(_miEnZh);
            dirMenu.DropDownItems.Add(_miZhEn);

            var themeMenu = new Forms.ToolStripMenuItem("外观");
            _miDark = new Forms.ToolStripMenuItem("深色卡片", null, (s, e) => ToggleTheme());
            _miDark.Checked = _cfg.DarkTheme;
            var miFontBig = new Forms.ToolStripMenuItem("大字号", null, (s, e) => SetFont(17));
            var miFontMid = new Forms.ToolStripMenuItem("中字号", null, (s, e) => SetFont(15));
            var miFontSmall = new Forms.ToolStripMenuItem("小字号", null, (s, e) => SetFont(13));
            var miShowSrc = new Forms.ToolStripMenuItem("显示原文", null, (s, e) => ToggleSource());
            var miQuiet = new Forms.ToolStripMenuItem("低调样式", null, (s, e) => ToggleQuiet());
            miQuiet.Checked = _cfg.QuietMode;
            var miAutoDim = new Forms.ToolStripMenuItem("闲置自动变淡", null, (s, e) => ToggleAutoDim());
            miAutoDim.Checked = _cfg.AutoDim;
            miShowSrc.Checked = _cfg.ShowOriginal;
            themeMenu.DropDownItems.Add(_miDark);
            themeMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            themeMenu.DropDownItems.Add(miFontBig);
            themeMenu.DropDownItems.Add(miFontMid);
            themeMenu.DropDownItems.Add(miFontSmall);
            themeMenu.DropDownItems.Add(new Forms.ToolStripSeparator());
            themeMenu.DropDownItems.Add(miShowSrc);
            themeMenu.DropDownItems.Add(miQuiet);
            themeMenu.DropDownItems.Add(miAutoDim);

            _miAutoStart = new Forms.ToolStripMenuItem("开机自动启动", null, (s, e) => ToggleAutoStart());
            _miAutoStart.Checked = IsAutoStartEnabled();

            var miOpenCfg = new Forms.ToolStripMenuItem("打开配置文件", null, (s, e) =>
            {
                try { Process.Start("explorer.exe", "/select,\"" + AppConfig.ConfigPath + "\""); } catch { }
            });

            var miHelp = new Forms.ToolStripMenuItem("快捷键说明", null, (s, e) =>
            {
                MessageBox.Show(
                    "鼠标停在任意文字上约 " + _cfg.HoverDelayMs + " 毫秒，自动弹出译文。\n\n" +
                    "Ctrl+Alt+T   立即翻译光标处的文字（取不到则翻剪贴板）\n" +
                    "Ctrl+Alt+D   切换翻译方向（自动 / 英译中 / 中译英）\n" +
                    "Ctrl+Alt+H   暂停或恢复悬停翻译\n\n" +
                    "卡片：按住左键可拖动，右键复制译文，右上角 × 关闭。",
                    "悬停翻译",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            });

            var miExit = new Forms.ToolStripMenuItem("退出", null, (s, e) => ExitApp());

            menu.Items.Add(_miHover);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(dirMenu);
            menu.Items.Add(themeMenu);
            menu.Items.Add(_miAutoStart);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(miOpenCfg);
            menu.Items.Add(miHelp);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(miExit);

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => ToggleHover();
            RefreshDirectionChecks();
        }

        private void SetDirection(Direction d)
        {
            _cfg.Direction = (int)d;
            _cfg.Save();
            RefreshDirectionChecks();
            ShowToast(d == Direction.Auto ? "方向：自动识别" : (d == Direction.EnToZh ? "方向：英译中" : "方向：中译英"));
        }

        private void RefreshDirectionChecks()
        {
            if (_miAuto == null) return;
            _miAuto.Checked = _cfg.Direction == (int)Direction.Auto;
            _miEnZh.Checked = _cfg.Direction == (int)Direction.EnToZh;
            _miZhEn.Checked = _cfg.Direction == (int)Direction.ZhToEn;
        }

        private void ToggleHover()
        {
            _cfg.HoverEnabled = !_cfg.HoverEnabled;
            _cfg.Save();
            _miHover.Checked = _cfg.HoverEnabled;
            if (!_cfg.HoverEnabled) _card.HideCard(true);
            ShowToast(_cfg.HoverEnabled ? "悬停翻译已开启" : "悬停翻译已暂停");
        }

        private void ToggleTheme()
        {
            _cfg.DarkTheme = !_cfg.DarkTheme;
            _cfg.Save();
            _miDark.Checked = _cfg.DarkTheme;
            RefreshCardStyle();
        }

        private void ToggleSource()
        {
            _cfg.ShowOriginal = !_cfg.ShowOriginal;
            _cfg.Save();
            ShowToast(_cfg.ShowOriginal ? "卡片显示原文" : "卡片隐藏原文");
        }

        private void ToggleQuiet()
        {
            _cfg.QuietMode = !_cfg.QuietMode;
            _cfg.Save();
            RefreshCardStyle();
            ShowToast(_cfg.QuietMode ? "低调样式" : "标准样式");
        }

        private void ToggleAutoDim()
        {
            _cfg.AutoDim = !_cfg.AutoDim;
            _cfg.Save();
            ShowToast(_cfg.AutoDim ? "闲置自动变淡" : "常亮显示");
        }

        private void RefreshCardStyle()
        {
            _card.QuietMode = _cfg.QuietMode;
            _card.ApplyTheme(_cfg.DarkTheme, _cfg.FontSize);
        }

        private void SetFont(double size)
        {
            _cfg.FontSize = size;
            _cfg.Save();
            RefreshCardStyle();
        }

        private void ShowToast(string text)
        {
            try
            {
                POINT p = Native.CursorPos();
                _card.ShowMessage(text, "Ctrl+Alt+H 暂停 · Ctrl+Alt+D 换方向");
                _card.ShowMessage(text, "");
                if (_card.IsVisible)
                {
                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                    t.Tick += (s, e) => { t.Stop(); _card.HideCard(false); };
                    t.Start();
                }
            }
            catch { }
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
            int next = (_cfg.Direction + 1) % 3;
            SetDirection((Direction)next);
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

            string from, to;
            Lang.Resolve((Direction)_cfg.Direction, text, out from, out to);

            _card.ShowLoading(text, Lang.DisplayPair(from, to), p.X, p.Y, _cfg.ShowOriginal);


            int req = _requestId;
            Direction dir = (Direction)_cfg.Direction;

            ThreadPool.QueueUserWorkItem(delegate
            {
                TranslationResult r = null;
                try { r = _service.Translate(text, dir); }
                catch (Exception ex) { r = new TranslationResult { Error = ex.Message }; }

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (req != _requestId) return;

                    _card.ShowResult(r, _cfg.ShowOriginal);
                    ScheduleAutoDim();
                    if (!_cfg.HoverEnabled) AutoHide(2200);
                }));
            });
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
