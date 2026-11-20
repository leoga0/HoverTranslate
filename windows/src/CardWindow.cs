using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace HoverTranslate
{
    /// <summary>
    /// 译文浮层。
    /// 设计目标是「存在感足够低」：不挡阅读视线、对比度收敛、闲置时自动退到背景里。
    /// </summary>
    public class CardWindow : Window
    {
        private const double MaxCardWidth = 360;
        private const double TextMaxWidth = 336;

        private readonly Border _root;
        private readonly TextBlock _pair;
        private readonly TextBlock _source;
        private readonly TextBlock _target;
        private readonly TextBlock _footer;
        private readonly Border _divider;
        private readonly TextBlock _closeBtn;
        private readonly StackPanel _sourceBox;
        private readonly DispatcherTimer _leaveTimer;

        private bool _dark = true;
        private double _fontSize = 14;
        private int _anchorX;
        private int _anchorY;
        private string _translated = "";
        private string _original = "";
        private bool _dimmed;

        public bool IsPinned { get; set; }
        public bool QuietMode { get; set; }
        public bool HasResult { get; set; }

        public CardWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Opacity = 0;

            _pair = new TextBlock
            {
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(105, 168, 200, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            _closeBtn = new TextBlock
            {
                Text = "×",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(3, 0, 0, 3)
            };
            _closeBtn.MouseLeftButtonDown += (s, e) => { e.Handled = true; HideCard(true); };
            _closeBtn.MouseEnter += (s, e) => { _closeBtn.Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)); };
            _closeBtn.MouseLeave += (s, e) => { _closeBtn.Foreground = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)); };

            var head = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 0) };
            DockPanel.SetDock(_pair, Dock.Left);
            head.Children.Add(_pair);
            head.Children.Add(_closeBtn);

            _source = new TextBlock
            {
                FontSize = _fontSize - 2.5,
                Foreground = new SolidColorBrush(Color.FromArgb(120, 230, 236, 248)),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = TextMaxWidth,
                LineHeight = (_fontSize - 2.5) * 1.45,
                Margin = new Thickness(0, 4, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            _divider = new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)),
                Margin = new Thickness(0, 5, 0, 0)
            };

            _sourceBox = new StackPanel();
            _sourceBox.Children.Add(_divider);
            _sourceBox.Children.Add(_source);

            _target = new TextBlock
            {
                FontSize = _fontSize,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = TextMaxWidth,
                LineHeight = _fontSize * 1.5,
                Margin = new Thickness(0, 5, 0, 0)
            };

            _footer = new TextBlock
            {
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromArgb(90, 220, 228, 240)),
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.NoWrap,
                Visibility = Visibility.Collapsed
            };

            var stack = new StackPanel();
            stack.Children.Add(head);
            stack.Children.Add(_sourceBox);
            stack.Children.Add(_target);
            stack.Children.Add(_footer);

            _root = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(206, 26, 28, 34)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(11, 7, 11, 8),
                Child = stack,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 10,
                    ShadowDepth = 1,
                    Direction = 270,
                    Opacity = 0.18,
                    Color = Colors.Black
                },
                MinWidth = 88,
                MaxWidth = MaxCardWidth
            };

            Content = _root;

            MouseLeftButtonDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    IsPinned = true;
                    try { DragMove(); } catch { }
                }
            };

            MouseRightButtonUp += (s, e) => CopyTarget();

            MouseEnter += (s, e) =>
            {
                _leaveTimer.Stop();
                if (_dimmed) SetDim(false);
            };

            MouseLeave += (s, e) =>
            {
                if (IsPinned) return;
                _leaveTimer.Stop();
                _leaveTimer.Start();
            };

            _leaveTimer = new DispatcherTimer();
            _leaveTimer.Interval = TimeSpan.FromMilliseconds(900);
            _leaveTimer.Tick += (s, e) =>
            {
                _leaveTimer.Stop();
                if (IsMouseOver) return;
                if (HasResult) SetDim(true);
            };

            SourceInitialized += (s, e) => Native.ApplyOverlayStyle(this);
        }

        // MARK: - 主题

        public void ApplyTheme(bool dark, double fontSize)
        {
            _dark = dark;
            _fontSize = fontSize;

            if (dark)
            {
                _root.Background = new SolidColorBrush(Color.FromArgb(QuietMode ? (byte)196 : (byte)224, 24, 26, 32));
                _root.BorderBrush = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
                _target.Foreground = new SolidColorBrush(Color.FromArgb(238, 255, 255, 255));
                _source.Foreground = new SolidColorBrush(Color.FromArgb(110, 226, 232, 244));
                _footer.Foreground = new SolidColorBrush(Color.FromArgb(92, 220, 228, 240));
                _pair.Foreground = new SolidColorBrush(Color.FromArgb(102, 164, 196, 252));
                _divider.Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255));
                _closeBtn.Foreground = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
            }
            else
            {
                _root.Background = new SolidColorBrush(Color.FromArgb(QuietMode ? (byte)224 : (byte)244, 252, 252, 254));
                _root.BorderBrush = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
                _target.Foreground = new SolidColorBrush(Color.FromRgb(32, 34, 40));
                _source.Foreground = new SolidColorBrush(Color.FromRgb(118, 124, 136));
                _footer.Foreground = new SolidColorBrush(Color.FromRgb(150, 156, 168));
                _pair.Foreground = new SolidColorBrush(Color.FromRgb(60, 110, 190));
                _divider.Background = new SolidColorBrush(Color.FromArgb(20, 0, 0, 0));
                _closeBtn.Foreground = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));
            }

            _root.Effect = QuietMode
                ? new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Direction = 270, Opacity = 0.12, Color = Colors.Black }
                : new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Direction = 270, Opacity = 0.32, Color = Colors.Black };

            _target.FontSize = _fontSize;
            _target.LineHeight = _fontSize * 1.5;
            _source.FontSize = Math.Max(10, _fontSize - 2.5);
            _source.LineHeight = Math.Max(10, _fontSize - 2.5) * 1.45;
        }

        // MARK: - 内容

        public void ShowLoading(string source, string pair, int anchorX, int anchorY, bool showSource)
        {
            _anchorX = anchorX;
            _anchorY = anchorY;
            _original = source ?? "";
            _translated = "";
            HasResult = false;
            _dimmed = false;

            _pair.Text = pair;
            _source.Text = TextShape.Clean(source, 400);
            _sourceBox.Visibility = showSource && _source.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _target.Text = "翻译中…";
            _target.Foreground = _dark
                ? new SolidColorBrush(Color.FromArgb(140, 200, 210, 230))
                : new SolidColorBrush(Color.FromRgb(130, 136, 148));
            _footer.Text = "";
            _footer.Visibility = Visibility.Collapsed;

            Present();
        }

        public void ShowResult(TranslationResult r, bool showSource)
        {
            HasResult = true;
            _translated = r == null ? "" : r.Target;
            _original = r == null ? _original : r.Source;

            if (r != null && r.Success)
            {
                _pair.Text = CompactPair(r);
                _source.Text = TextShape.Clean(r.Source, 400);
                _sourceBox.Visibility = showSource && _source.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                _target.Text = r.Target;
                _target.Foreground = _dark
                    ? new SolidColorBrush(Color.FromArgb(238, 255, 255, 255))
                    : new SolidColorBrush(Color.FromRgb(32, 34, 40));
                _footer.Text = "";
                _footer.Visibility = Visibility.Collapsed;
            }
            else
            {
                _sourceBox.Visibility = Visibility.Collapsed;
                _target.Text = "翻译失败";
                _footer.Text = r == null ? "" : r.Error;
                _footer.Visibility = string.IsNullOrEmpty(_footer.Text) ? Visibility.Collapsed : Visibility.Visible;
                _target.Foreground = _dark
                    ? new SolidColorBrush(Color.FromRgb(240, 130, 120))
                    : new SolidColorBrush(Color.FromRgb(185, 55, 45));
            }
            Present();
        }

        public void ShowMessage(string message, string detail)
        {
            HasResult = false;
            _dimmed = false;
            _pair.Text = "";
            _sourceBox.Visibility = Visibility.Collapsed;
            _target.Text = message;
            _target.Foreground = _dark
                ? new SolidColorBrush(Color.FromArgb(238, 255, 255, 255))
                : new SolidColorBrush(Color.FromRgb(32, 34, 40));
            _footer.Text = detail ?? "";
            _footer.Visibility = string.IsNullOrEmpty(_footer.Text) ? Visibility.Collapsed : Visibility.Visible;
            Present();
        }

        /// <summary>顶部只留「英→中 · 引擎」，把信息压到一行，避免多一行说明文字。</summary>
        private string CompactPair(TranslationResult r)
        {
            string pair = Lang.DisplayPair(r.From, r.To);
            if (string.IsNullOrEmpty(r.Engine)) return pair;
            string engine = r.Engine;
            if (engine.EndsWith("翻译")) engine = engine.Substring(0, engine.Length - 2);
            if (engine.Length > 6) engine = engine.Substring(0, 6);
            return pair + " · " + engine;
        }

        private void Present()
        {
            if (!IsVisible)
            {
                Opacity = 0;
                Show();
            }
            UpdateLayout();
            Reposition();
            FadeTo(1.0, 110);

            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (!IsVisible) return;
                UpdateLayout();
                Reposition();
            }), DispatcherPriority.Render);
        }

        // MARK: - 定位

        /// <summary>
        /// 放在鼠标右侧、垂直与鼠标对齐。这样不会压住鼠标下方正在读的下一行。
        /// 右侧放不下就翻到左侧。
        /// </summary>
        public void Reposition()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            RECT r;
            if (!Native.GetWindowRect(hwnd, out r)) return;
            int w = r.Width, h = r.Height;
            if (w <= 0 || h <= 0) return;

            System.Drawing.Rectangle scr = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(_anchorX, _anchorY)).WorkingArea;

            int gap = 22;
            int x = _anchorX + gap;
            int y = _anchorY - h / 2;

            if (x + w > scr.Right - 8) x = _anchorX - w - gap;
            if (x < scr.Left + 8) x = scr.Left + 8;

            if (y + h > scr.Bottom - 8) y = scr.Bottom - h - 8;
            if (y < scr.Top + 8) y = scr.Top + 8;

            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        // MARK: - 显隐与透明度

        /// <summary>闲置时把卡片压到接近背景，需要时鼠标移上去恢复。</summary>
        public void SetDim(bool on)
        {
            if (!IsVisible) return;
            _dimmed = on;
            FadeTo(on ? 0.42 : 1.0, on ? 320 : 140);
        }

        public void HideCard(bool immediate)
        {
            if (!IsVisible) return;
            IsPinned = false;
            HasResult = false;
            _dimmed = false;
            _leaveTimer.Stop();
            if (immediate)
            {
                Opacity = 0;
                Hide();
            }
            else
            {
                FadeTo(0, 130, delegate
                {
                    if (Opacity < 0.05) Hide();
                });
            }
        }

        private void FadeTo(double to, int ms, Action done = null)
        {
            var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
            if (done != null) anim.Completed += (s, e) => done();
            BeginAnimation(OpacityProperty, anim);
        }

        public bool ContainsCursor(int x, int y)
        {
            if (!IsVisible) return false;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            RECT r;
            if (!Native.GetWindowRect(hwnd, out r)) return false;
            return x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom;
        }

        private void CopyTarget()
        {
            string text = string.IsNullOrEmpty(_translated) ? _target.Text : _translated;
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); } catch { }
        }
    }
}
