using System;
using System.Collections.Generic;
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
        private const double MaxCardWidth = 420;
        private const double TextMaxWidth = 396;

        private readonly Border _root;
        private readonly TextBlock _pair;
        private readonly TextBlock _source;
        private readonly TextBlock _target;
        private readonly TextBlock _footer;
        private readonly Border _divider;
        private readonly Border _termDivider;
        private readonly StackPanel _termBox;
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
        private int _wantW = 360;
        private int _wantH = 60;

        public bool IsPinned { get; set; }
        public bool QuietMode { get; set; }
        public int CardAnchor { get; set; }
        public bool HasResult { get; set; }

        /// <summary>
        /// 是否允许「闲置自动变淡」。以前这里没有开关，鼠标一离开卡片就走 _leaveTimer 直接压暗，
        /// 菜单里关掉自动变淡也没用——判定必须由这个开关统一管。
        /// </summary>
        public bool AutoDimEnabled { get; set; }

        /// <summary>当前是否已被压暗，自检用。</summary>
        public bool IsDimmed { get { return _dimmed; } }

        public CardWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Manual;
            Width = MaxCardWidth;
            Height = 80;
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

            _termDivider = new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                Margin = new Thickness(0, 7, 0, 0),
                Visibility = Visibility.Collapsed
            };

            _termBox = new StackPanel { Visibility = Visibility.Collapsed };

            var stack = new StackPanel();
            stack.Children.Add(head);
            stack.Children.Add(_sourceBox);
            stack.Children.Add(_target);
            stack.Children.Add(_termDivider);
            stack.Children.Add(_termBox);
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
                if (AutoDimEnabled && HasResult) SetDim(true);
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
                _termDivider.Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255));
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
                _termDivider.Background = new SolidColorBrush(Color.FromArgb(16, 0, 0, 0));
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
            ShowTerms(null);

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
            ShowMessageAt(message, detail, _anchorX, _anchorY);
        }

        /// <summary>在指定位置显示一条提示（开关反馈用），位置跟鼠标走。</summary>
        public void ShowMessageAt(string message, string detail, int anchorX, int anchorY)
        {
            _anchorX = anchorX;
            _anchorY = anchorY;
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

        /// <summary>在译文下方补一段术语解释：先用一句话讲清楚，再打个比方。</summary>
        public void ShowTerms(List<Terminology.Term> terms)
        {
            _termBox.Children.Clear();

            if (terms == null || terms.Count == 0)
            {
                _termBox.Visibility = Visibility.Collapsed;
                _termDivider.Visibility = Visibility.Collapsed;
                return;
            }

            int shown = 0;
            foreach (Terminology.Term term in terms)
            {
                if (shown >= 3) break;

                var head = new TextBlock
                {
                    Text = string.IsNullOrEmpty(term.c) ? term.n : term.n + " · " + term.c,
                    FontSize = 9.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = _dark
                        ? new SolidColorBrush(Color.FromArgb((byte)(shown == 0 ? 150 : 110), 150, 190, 255))
                        : new SolidColorBrush(Color.FromArgb((byte)(shown == 0 ? 220 : 160), 40, 90, 170)),
                    Margin = new Thickness(0, shown == 0 ? 0 : 6, 0, 1)
                };

                string bodyText = term.d;
                if (!string.IsNullOrEmpty(term.x)) bodyText += "\n" + term.x;

                var body = new TextBlock
                {
                    Text = bodyText,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = TextMaxWidth,
                    LineHeight = 16,
                    Foreground = _dark
                        ? new SolidColorBrush(Color.FromArgb((byte)(shown == 0 ? 200 : 150), 226, 232, 244))
                        : new SolidColorBrush(Color.FromArgb((byte)(shown == 0 ? 230 : 180), 52, 56, 66))
                };

                _termBox.Children.Add(head);
                _termBox.Children.Add(body);
                shown++;
            }

            _termBox.Visibility = Visibility.Visible;
            _termDivider.Visibility = Visibility.Visible;
            Relayout();
        }

        /// <summary>
        /// 内容变了就重新量一次尺寸并摆位。
        /// 透明无边框窗口用 WPF 的 Width/Height 改大小并不可靠，这里直接算成物理像素交给 SetWindowPos。
        /// </summary>
        private void Relayout()
        {
            if (!IsVisible) return;

            // 宽度固定：一旦按内容收缩，测量用的宽度和实际渲染宽度就会不一致，
            // 越高越窄、越窄越高，最后挤成一条竖排。固定宽度后高度才是准的。
            double cardW = MaxCardWidth;
            _root.Width = cardW - 2;

            // 刚刚改过 Visibility / 加过子元素，必须作废旧的测量结果，否则拿到的是缓存高度
            _root.InvalidateMeasure();
            _root.InvalidateArrange();
            _root.Measure(new Size(cardW - 2, double.PositiveInfinity));
            _root.Arrange(new Rect(0, 0, cardW - 2, _root.DesiredSize.Height));

            double h = Math.Max(56, Math.Ceiling(_root.DesiredSize.Height) + 6);
            if (double.IsNaN(h)) return;

            double scale = Native.GetScaleForPoint(_anchorX, _anchorY);
            if (scale <= 0) scale = 1;

            _wantW = (int)Math.Ceiling(cardW * scale);
            _wantH = (int)Math.Ceiling(h * scale);

            Reposition();

            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (!IsVisible) return;
                Reposition();
            }), DispatcherPriority.Loaded);
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
            Relayout();
            FadeTo(1.0, 110);
        }

        // MARK: - 定位

        /// <summary>
        /// 卡片定位。默认放鼠标左上方——那里是已经读过的内容，不会挡到后面和下面的句子。
        /// 放不下就按 右上 → 左下 → 右下 的顺序找位置；也可以固定到屏幕右下角。
        /// </summary>
        public void Reposition()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int w = _wantW, h = _wantH;
            if (w <= 0 || h <= 0) return;

            System.Drawing.Rectangle scr = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point(_anchorX, _anchorY)).WorkingArea;

            int margin = 8;
            int gap = 16;
            int x, y;

            if (CardAnchor == 2)
            {
                x = scr.Right - w - 16;
                y = scr.Bottom - h - 16;
            }
            else if (CardAnchor == 1)
            {
                x = _anchorX + gap + 6;
                y = _anchorY - h / 2;
                if (x + w > scr.Right - margin) x = _anchorX - w - gap - 6;
            }
            else
            {
                // 左上优先，其次右上、左下、右下
                int leftX = _anchorX - w - gap;
                int rightX = _anchorX + gap;
                int upY = _anchorY - h - gap;
                int downY = _anchorY + gap;

                x = leftX;
                y = upY;

                if (x < scr.Left + margin) x = rightX;
                if (x + w > scr.Right - margin) x = leftX;

                if (y < scr.Top + margin) y = downY;
                if (y + h > scr.Bottom - margin) y = upY;

                if (x < scr.Left + margin) x = scr.Left + margin;
                if (x + w > scr.Right - margin) x = scr.Right - w - margin;
                if (y < scr.Top + margin) y = scr.Top + margin;
            }

            if (y + h > scr.Bottom - margin) y = scr.Bottom - h - margin;
            if (y < scr.Top + margin) y = scr.Top + margin;
            if (x < scr.Left + margin) x = scr.Left + margin;

            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, w, h,
                Native.SWP_NOACTIVATE);
        }

        // MARK: - 显隐与透明度

        /// <summary>闲置时把卡片压到接近背景，需要时鼠标移上去恢复。开关关掉时任何路径都不许压暗。</summary>
        public void SetDim(bool on)
        {
            if (!IsVisible) return;
            if (on && !AutoDimEnabled) return;
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

        /// <summary>把卡片内容离屏渲染成位图，自检出图用，不依赖屏幕截图。</summary>
        public System.Windows.Media.Imaging.BitmapSource Snapshot(int width)
        {
            _root.Width = width - 2;
            _root.InvalidateMeasure();
            _root.InvalidateArrange();
            _root.Measure(new Size(width - 2, double.PositiveInfinity));
            _root.Arrange(new Rect(0, 0, width - 2, _root.DesiredSize.Height));
            _root.UpdateLayout();

            int h = (int)Math.Ceiling(_root.DesiredSize.Height);
            if (h < 20) h = 20;
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(width, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(_root);
            return bmp;
        }
    }
}
