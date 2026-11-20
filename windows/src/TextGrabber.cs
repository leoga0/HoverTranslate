using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Automation;

namespace HoverTranslate
{
    public class GrabResult
    {
        public bool Found;
        public string Text = "";
        public string Control = "";
        public string Route = "";
        public RECT Rect;
        public int X;
        public int Y;
    }

    /// <summary>
    /// 光标取词：用 UI Automation 读取光标下控件的文字。
    /// TextPattern 相关类型在部分系统上被裁剪，故一律走反射调用。
    /// </summary>
    public static class TextGrabber
    {
        private static readonly int OwnPid = Process.GetCurrentProcess().Id;

        // 只翻译“内容型”控件；按钮、菜单项、标签页等交互控件不触发，避免鼠标掠过按钮时乱弹卡片
        private static readonly HashSet<string> TextControlTypes = new HashSet<string>
        {
            "Text", "Link", "Edit", "ListItem", "HeaderItem", "DataItem", "TreeItem", "Hyperlink"
        };

        // 交互控件的控件名不当作待翻译文本
        private static readonly HashSet<string> SkipTypes = new HashSet<string>
        {
            "Button", "MenuItem", "TabItem", "CheckBox", "RadioButton", "SplitButton",
            "ToolBar", "Menu", "ScrollBar", "Thumb", "TitleBar", "StatusBar", "ProgressBar"
        };

        private static readonly HashSet<string> ContainerTypes = new HashSet<string>
        {
            "Pane", "Group", "Custom", "Window", "Document", "List", "Table",
            "DataGrid", "Tree", "ToolBar", "Menu", "ScrollBar", "TitleBar", "Header"
        };

        public static GrabResult Grab(int x, int y, int maxLen)
        {
            var result = new GrabResult { X = x, Y = y };
            string uiaFailure = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                AutomationElement el = AutomationElement.FromPoint(new System.Windows.Point(x, y));
                if (el != null)
                {
                    // 光标落在自己窗口上时不做取词
                    try
                    {
                        int pid = el.Current.ProcessId;
                        if (pid == OwnPid) { result.Route = "self"; return result; }
                    }
                    catch { }

                    AutomationElement cur = el;
                    for (int depth = 0; depth < 5 && cur != null; depth++)
                    {
                        string text;
                        string route;
                        if (TryRead(cur, x, y, out text, out route))
                        {
                            string cleaned = TextShape.Clean(text, maxLen);
                            if (TextShape.Worth(cleaned))
                            {
                                result.Found = true;
                                result.Text = cleaned;
                                result.Route = route;
                                result.Control = SafeControlName(cur);
                                result.Rect = SafeRect(cur);
                                return result;
                            }
                        }
                        try { cur = TreeWalker.ControlViewWalker.GetParent(cur); }
                        catch { cur = null; }
                    }
                }
            }
            catch (Exception ex)
            {
                uiaFailure = "uia:" + ex.GetType().Name;
                AppConfig.Log("grab@" + x + "," + y + " " + ex.GetType().FullName + " hr=0x" + ex.HResult.ToString("X8") + " :: " + ex.ToString());
            }

            // UIA 走不通时（Chromium 系浏览器等），用 MSAA 直接取点命中的文本
            if (sw.ElapsedMilliseconds > 900) return result;
            try
            {
                MsaaGrabber.MsaaHit hit = MsaaGrabber.HitTest(x, y);
                if (hit != null)
                {
                    result.Route = hit.Route;
                    result.Control = "msaa";
                    string candidate = !string.IsNullOrEmpty(hit.Text) ? hit.Text : hit.Value;
                    string cleaned = TextShape.Clean(candidate, maxLen);
                    if (TextShape.Worth(cleaned))
                    {
                        result.Found = true;
                        result.Text = cleaned;
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                AppConfig.Log("msaa@" + x + "," + y + " " + ex.Message);
            }

            if (uiaFailure != null && string.IsNullOrEmpty(result.Route)) result.Route = uiaFailure;
            return result;
        }

        private static bool TryRead(AutomationElement el, int x, int y, out string text, out string route)
        {
            text = null;
            route = "";

            string type = "";
            try { type = el.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""); }
            catch { }

            // 1. 文本类元素：Name 即文本
            if (TextControlTypes.Contains(type))
            {
                string name = SafeName(el);
                if (!string.IsNullOrEmpty(name))
                {
                    // 编辑框类优先取 Value
                    if (type == "Edit")
                    {
                        string v = TryValuePattern(el);
                        if (!string.IsNullOrEmpty(v)) { text = v; route = "value"; return true; }
                    }
                    text = name;
                    route = "name:" + type;
                    return true;
                }
            }

            // 2. 文本模式（Word / WPS / 浏览器文档 / 记事本）
            string txt = TryTextPattern(el, x, y);
            if (!string.IsNullOrEmpty(txt))
            {
                text = txt;
                route = "textpattern:" + type;
                return true;
            }

            // 3. 值模式
            string val = TryValuePattern(el);
            if (!string.IsNullOrEmpty(val) && val.Trim().Length > 0)
            {
                text = val;
                route = "value:" + type;
                return true;
            }

            // 4. 容器：向下找包含光标点的文本子元素
            if (ContainerTypes.Contains(type) || string.IsNullOrEmpty(type))
            {
                AutomationElement child = FindTextChildAt(el, x, y);
                if (child != null)
                {
                    string name = SafeName(child);
                    if (!string.IsNullOrEmpty(name))
                    {
                        text = name;
                        route = "child:" + SafeControlName(child);
                        return true;
                    }
                }
            }

            // 5. 自定义控件（微信、QQ、部分国产软件）常把正文放在 Name 上
            if (!SkipTypes.Contains(type))
            {
                string own = SafeName(el);
                if (!string.IsNullOrEmpty(own) && own.Length >= 2 && !LooksLikeChrome(own))
                {
                    text = own;
                    route = "name-generic:" + type;
                    return true;
                }
            }

            // 6. 兜底：HelpText
            try
            {
                string help = el.Current.HelpText;
                if (!string.IsNullOrEmpty(help) && help.Trim().Length > 2)
                {
                    text = help;
                    route = "helptext:" + type;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static AutomationElement FindTextChildAt(AutomationElement el, int x, int y)
        {
            AutomationElementCollection kids;
            try { kids = el.FindAll(TreeScope.Children, Condition.TrueCondition); }
            catch { return null; }

            if (kids == null) return null;

            AutomationElement fallback = null;
            foreach (AutomationElement k in kids)
            {
                RECT r = SafeRect(k);
                if (r.Width <= 0 || r.Height <= 0) continue;
                bool inside = x >= r.Left && x <= r.Right && y >= r.Top && y <= r.Bottom;
                string name = SafeName(k);
                string ktype = "";
                try { ktype = k.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""); } catch { }

                if (inside && !string.IsNullOrEmpty(name) && TextControlTypes.Contains(ktype))
                    return k;

                if (inside && fallback == null && !string.IsNullOrEmpty(name))
                    fallback = k;

                if (inside)
                {
                    AutomationElement deeper = FindTextChildAt(k, x, y);
                    if (deeper != null) return deeper;
                }
            }
            return fallback;
        }

        /// <summary>过滤掉窗口标题、软件名这类无翻译价值的整块界面文字。</summary>
        private static bool LooksLikeChrome(string s)
        {
            if (string.IsNullOrEmpty(s)) return true;
            if (s.Length > 300) return true;
            if (s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (s == "最小化" || s == "最大化" || s == "关闭" || s == "还原" || s == "系统菜单") return true;
            return false;
        }

        private static string SafeName(AutomationElement el)

        {
            try
            {
                string n = el.Current.Name;
                return n == null ? "" : n.Trim();
            }
            catch { return ""; }
        }

        private static string SafeControlName(AutomationElement el)
        {
            try { return el.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""); }
            catch { return ""; }
        }

        private static RECT SafeRect(AutomationElement el)
        {
            try
            {
                System.Windows.Rect r = el.Current.BoundingRectangle;
                if (double.IsInfinity(r.X) || double.IsNaN(r.X)) return new RECT();
                return new RECT
                {
                    Left = (int)r.Left,
                    Top = (int)r.Top,
                    Right = (int)r.Right,
                    Bottom = (int)r.Bottom
                };
            }
            catch { return new RECT(); }
        }

        private static string TryValuePattern(AutomationElement el)
        {
            try
            {
                object pat = el.GetCurrentPattern(ValuePattern.Pattern);
                if (pat == null) return null;
                PropertyInfo pi = pat.GetType().GetProperty("Current");
                if (pi == null) return null;
                object current = pi.GetValue(pat, null);
                if (current == null) return null;
                PropertyInfo vp = current.GetType().GetProperty("Value");
                if (vp == null) return null;
                object v = vp.GetValue(current, null);
                return v == null ? null : v.ToString();
            }
            catch { return null; }
        }

        /// <summary>
        /// 通过反射读取 TextPattern：RangeFromPoint -> ExpandToEnclosingUnit -> GetText。
        /// unit: 0 字符 / 1 格式 / 2 词 / 3 行 / 4 段
        /// </summary>
        public static string TryTextPattern(AutomationElement el, int x, int y)
        {
            try
            {
                object pat = el.GetCurrentPattern(TextPattern.Pattern);
                if (pat == null) return null;

                MethodInfo rangeFromPoint = pat.GetType().GetMethod("RangeFromPoint", new[] { typeof(System.Windows.Point) });
                if (rangeFromPoint == null) return null;

                object range = rangeFromPoint.Invoke(pat, new object[] { new System.Windows.Point(x, y) });
                if (range == null) return null;

                MethodInfo clone = range.GetType().GetMethod("Clone", Type.EmptyTypes);
                MethodInfo expand = range.GetType().GetMethod("ExpandToEnclosingUnit");
                MethodInfo getText = range.GetType().GetMethod("GetText");

                if (clone == null || expand == null || getText == null) return null;

                ParameterInfo[] eps = expand.GetParameters();
                Type unitType = eps.Length > 0 ? eps[0].ParameterType : null;

                // 先按行取，行内空则退回按词取
                int[] order = { 3, 2, 4 };
                foreach (int unit in order)
                {
                    try
                    {
                        object work = clone.Invoke(range, null);
                        object unitVal = unitType != null && unitType.IsEnum
                            ? Enum.ToObject(unitType, unit)
                            : (object)unit;
                        expand.Invoke(work, new[] { unitVal });
                        object txt = getText.Invoke(work, new object[] { -1 });
                        string s = txt == null ? "" : txt.ToString();
                        s = s == null ? "" : s.Trim();
                        if (s.Length > 0) return s;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }
    }
}
