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
    public class CardWindow : Window
    {
        private readonly Border _root;
        private readonly TextBlock _pair;
        private readonly TextBlock _source;
        private readonly TextBlock _target;
        private readonly TextBlock _footer;
        private readonly StackPanel _sourceBox;
        private readonly DispatcherTimer _fadeTimer;
        private bool _dark = true;
        private double _fontSize = 15;
        private int _anchorX;
        private int _anchorY;
        private string _translated = "";
        private string _original = "";

        public bool IsPinned { get; set; }

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
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };

            var closeBtn = new TextBlock
            {
                Text = "×",
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 0, 0, 4)
            };
            closeBtn.MouseLeftButtonDown += (s, e) => { e.Handled = true; HideCard(true); };
            closeBtn.MouseEnter += (s, e) => { closeBtn.Foreground = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)); };
            closeBtn.MouseLeave += (s, e) => { closeBtn.Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)); };

            var head = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(_pair, Dock.Left);
            head.Children.Add(_pair);
            head.Children.Add(closeBtn);

            _source = new TextBlock
            {
                FontSize = _fontSize - 2,
                Foreground = new SolidColorBrush(Color.FromArgb(150, 235, 240, 250)),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 430,
                LineHeight = (_fontSize - 2) * 1.55,
                Margin = new Thickness(0, 7, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            _sourceBox = new StackPanel();
            _sourceBox.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
                Margin = new Thickness(0, 7, 0, 0)
            });
            _sourceBox.Children.Add(_source);

            _target = new TextBlock
            {
                FontSize = _fontSize,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 430,
                LineHeight = _fontSize * 1.6,
                Margin = new Thickness(0, 8, 0, 0)
            };

            _footer = new TextBlock
            {
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromArgb(120, 220, 228, 240)),
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.NoWrap
            };

            var stack = new StackPanel();
            stack.Children.Add(head);
            stack.Children.Add(_sourceBox);
            stack.Children.Add(_target);
            stack.Children.Add(_footer);

            _root = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromArgb(240, 28, 30, 38)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 10, 14, 11),
                Child = stack,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 3,
                    Direction = 270,
                    Opacity = 0.45,
                    Color = Colors.Black
                },
                MinWidth = 180,
                MaxWidth = 470
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

            MouseEnter += (s, e) => { _fadeTimer.Stop(); };
            MouseLeave += (s, e) =>
            {
                if (IsPinned) return;
                _fadeTimer.Stop();
                _fadeTimer.Start();
            };
            MouseRightButtonUp += (s, e) => CopyTarget();

            _fadeTimer = new DispatcherTimer();
            _fadeTimer.Interval = TimeSpan.FromMilliseconds(650);
            _fadeTimer.Tick += (s, e) =>
            {
                _fadeTimer.Stop();
                if (IsMouseOver) return;
                HideCard(false);
            };

            SourceInitialized += (s, e) => Native.ApplyOverlayStyle(this);
        }

        public void ApplyTheme(bool dark, double fontSize)
        {
            _dark = dark;
            _fontSize = fontSize;
            if (dark)
            {
                _root.Background = new SolidColorBrush(Color.FromArgb(240, 28, 30, 38));
                _root.BorderBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255));
                _target.Foreground = Brushes.White;
                _source.Foreground = new SolidColorBrush(Color.FromArgb(150, 235, 240, 250));
                _footer.Foreground = new SolidColorBrush(Color.FromArgb(120, 220, 228, 240));
                _pair.Foreground = new SolidColorBrush(Color.FromArgb(180, 150, 190, 255));
            }
            else
            {
                _root.Background = new SolidColorBrush(Color.FromArgb(246, 252, 252, 254));
                _root.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
                _target.Foreground = new SolidColorBrush(Color.FromRgb(24, 26, 32));
                _source.Foreground = new SolidColorBrush(Color.FromRgb(96, 102, 116));
                _footer.Foreground = new SolidColorBrush(Color.FromRgb(140, 146, 158));
                _pair.Foreground = new SolidColorBrush(Color.FromRgb(28, 100, 210));
            }
            _target.FontSize = _fontSize;
            _target.LineHeight = _fontSize * 1.6;
            _source.FontSize = _fontSize - 2;
            _source.LineHeight = (_fontSize - 2) * 1.55;
        }

        public void ShowLoading(string source, string pair, int anchorX, int anchorY, bool showSource)
        {
            _anchorX = anchorX;
            _anchorY = anchorY;
            _original = source ?? "";
            _translated = "";
            _pair.Text = pair;
            _source.Text = TextShape.Clean(source, 400);
            _sourceBox.Visibility = showSource && _source.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _target.Text = "翻译中…";
            _target.Foreground = _dark
                ? new SolidColorBrush(Color.FromArgb(150, 200, 210, 230))
                : new SolidColorBrush(Color.FromRgb(120, 126, 140));
            _footer.Text = "";
            Present();
        }

        public void ShowResult(TranslationResult r, bool showSource)
        {
            _translated = r == null ? "" : r.Target;
            _original = r == null ? _original : r.Source;
            if (r != null && r.Success)
            {
                _pair.Text = Lang.DisplayPair(r.From, r.To);
                _source.Text = TextShape.Clean(r.Source, 400);
                _sourceBox.Visibility = showSource && _source.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                _target.Text = r.Target;
                _target.Foreground = _dark ? Brushes.White : new SolidColorBrush(Color.FromRgb(24, 26, 32));
                _footer.Text = "引擎 " + r.Engine + "   ·   右键复制译文   ·   按住可拖动";
            }
            else
            {
                _target.Text = "翻译失败";
                _footer.Text = r == null ? "" : r.Error;
                _target.Foreground = _dark
                    ? new SolidColorBrush(Color.FromRgb(255, 138, 128))
                    : new SolidColorBrush(Color.FromRgb(190, 50, 40));
            }
            Present();
        }

        public void ShowMessage(string message, string detail)
        {
            _pair.Text = "";
            _sourceBox.Visibility = Visibility.Collapsed;
            _target.Text = message;
            _target.Foreground = _dark ? Brushes.White : new SolidColorBrush(Color.FromRgb(24, 26, 32));
            _footer.Text = detail ?? "";
            Present();
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
            FadeTo(1, 110);

            // 内容变化后窗口尺寸会再变一次，等布局落定再校正位置，避免卡片被裁切或越界
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (!IsVisible) return;
                UpdateLayout();
                Reposition();
            }), DispatcherPriority.Render);
        }

        public void Reposition()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            RECT r;
            if (!Native.GetWindowRect(hwnd, out r)) return;
            int w = r.Width, h = r.Height;
            if (w <= 0 || h <= 0) return;

            System.Drawing.Rectangle scr = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(_anchorX, _anchorY)).WorkingArea;

            int x = _anchorX + 18;
            int y = _anchorY + 24;

            if (x + w > scr.Right - 8) x = _anchorX - w - 18;
            if (y + h > scr.Bottom - 8) y = _anchorY - h - 24;
            if (x < scr.Left + 8) x = scr.Left + 8;
            if (y < scr.Top + 8) y = scr.Top + 8;

            Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, 0, 0,
                Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public void HideCard(bool immediate)
        {
            if (!IsVisible) return;
            IsPinned = false;
            _fadeTimer.Stop();
            if (immediate)
            {
                Opacity = 0;
                Hide();
            }
            else
            {
                FadeTo(0, 140, () =>
                {
                    if (Opacity < 0.05) Hide();
                });
            }
        }

        private void FadeTo(double to, int ms, Action done = null)
        {
            var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
            anim.Completed += (s, e) => { if (done != null) done(); };
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
            string old = _footer.Text;
            _footer.Text = "已复制译文";
            DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
            t.Tick += (s, e) => { t.Stop(); if (IsVisible) _footer.Text = old; };
            t.Start();
        }
    }
}
