using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HoverTranslate
{
    /// <summary>小型输入对话框：多字段，确定返回填写值，取消返回 null。</summary>
    public static class Prompt
    {
        public static string[] Ask(string title, string[] labels, string[] initial, string hint)
        {
            var win = new Window
            {
                Title = title,
                Width = 460,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true,
                Background = new SolidColorBrush(Color.FromRgb(30, 32, 38))
            };

            var root = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };

            if (!string.IsNullOrEmpty(hint))
            {
                root.Children.Add(new TextBlock
                {
                    Text = hint,
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 176, 220)),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                });
            }

            var boxes = new TextBox[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                root.Children.Add(new TextBlock
                {
                    Text = labels[i],
                    Foreground = new SolidColorBrush(Color.FromRgb(196, 204, 220)),
                    FontSize = 12,
                    Margin = new Thickness(0, i == 0 ? 0 : 10, 0, 4)
                });

                bool longField = labels[i].IndexOf("解释") >= 0 || labels[i].IndexOf("比方") >= 0;
                var tb = new TextBox
                {
                    Text = initial != null && i < initial.Length ? (initial[i] ?? "") : "",
                    FontSize = 13,
                    Padding = new Thickness(6, 5, 6, 5),
                    Background = new SolidColorBrush(Color.FromRgb(44, 47, 56)),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(70, 76, 92)),
                    CaretBrush = Brushes.White,
                    AcceptsReturn = longField,
                    TextWrapping = longField ? TextWrapping.Wrap : TextWrapping.NoWrap,
                    MinHeight = longField ? 54 : 28,
                    VerticalContentAlignment = longField ? VerticalAlignment.Top : VerticalAlignment.Center
                };
                boxes[i] = tb;
                root.Children.Add(tb);
            }

            var bar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };

            string[] result = null;

            var cancel = new Button { Content = "取消", Width = 74, Height = 30, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            var ok = new Button { Content = "确定", Width = 74, Height = 30, IsDefault = true };

            ok.Click += delegate
            {
                result = new string[boxes.Length];
                for (int i = 0; i < boxes.Length; i++) result[i] = boxes[i].Text;
                win.Close();
            };
            cancel.Click += delegate { win.Close(); };

            bar.Children.Add(cancel);
            bar.Children.Add(ok);
            root.Children.Add(bar);

            win.Content = root;
            win.Loaded += delegate { if (boxes.Length > 0) boxes[0].Focus(); };
            win.ShowDialog();

            return result;
        }
    }
}
