using System;
using System.Runtime.InteropServices;
using Accessibility;

namespace HoverTranslate
{
    /// <summary>
    /// MSAA 取词通道。UIA 在部分应用（Chromium 系浏览器、部分国产软件）上会走到
    /// MSAA 代理并被拒绝访问，此时直接用 IAccessible 取点命中的文本更可靠。
    /// 浏览器首次访问只暴露 legacy 容器，需要遍历子对象才能拿到文本节点。
    /// </summary>
    internal static class MsaaGrabber
    {
        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromPoint(
            POINT pt,
            [MarshalAs(UnmanagedType.Interface)] out object ppacc,
            [MarshalAs(UnmanagedType.Struct)] out object pvarChild);

        private const int ROLE_SYSTEM_STATICTEXT = 0x29;
        private const int ROLE_SYSTEM_TEXT = 0x2A;
        private const int ROLE_SYSTEM_BUTTON = 0x2B;
        private const int ROLE_SYSTEM_MENUITEM = 0xC;
        private const int ROLE_SYSTEM_TAB = 0x25;
        private const int ROLE_SYSTEM_CHECKBUTTON = 0x2C;
        private const int ROLE_SYSTEM_LINK = 0x1E;
        private const int ROLE_SYSTEM_CLIENT = 0xA;
        private const int ROLE_SYSTEM_DOCUMENT = 0xF;
        private const int ROLE_SYSTEM_PANE = 0x10;

        public class MsaaHit
        {
            public string Text = "";
            public string Value = "";
            public int Role;
            public string Route = "";
        }

        public static MsaaHit HitTest(int x, int y)
        {
            try
            {
                object accObj;
                object childObj;
                int hr = AccessibleObjectFromPoint(new POINT(x, y), out accObj, out childObj);
                if (hr != 0 || accObj == null) return null;

                IAccessible acc = accObj as IAccessible;
                if (acc == null) return null;

                int child = 0;
                try { if (childObj is int) child = (int)childObj; } catch { }

                var hit = new MsaaHit();
                try
                {
                    object role = acc.get_accRole(child);
                    if (role != null) hit.Role = Convert.ToInt32(role);
                }
                catch { }

                if (hit.Role == ROLE_SYSTEM_BUTTON || hit.Role == ROLE_SYSTEM_MENUITEM ||
                    hit.Role == ROLE_SYSTEM_TAB || hit.Role == ROLE_SYSTEM_CHECKBUTTON)
                {
                    hit.Route = "msaa-skip:role" + hit.Role;
                    return hit;
                }

                try { hit.Text = (acc.get_accName(child) ?? "").Trim(); }
                catch { hit.Text = ""; }

                try { hit.Value = (acc.get_accValue(child) ?? "").Trim(); }
                catch { hit.Value = ""; }

                hit.Route = "msaa:role" + hit.Role;

                bool isContainer = (hit.Role == ROLE_SYSTEM_CLIENT || hit.Role == ROLE_SYSTEM_DOCUMENT ||
                                    hit.Role == ROLE_SYSTEM_PANE) && hit.Text.Length < 3;

                // 容器节点（浏览器 legacy window）：向下遍历子对象找文本节点
                if (isContainer && child == 0)
                {
                    int budget = 200;
                    string deep = DeepFindText(acc, 0, ref budget);
                    if (!string.IsNullOrEmpty(deep))
                    {
                        hit.Text = deep;
                        hit.Route = "msaa-deep:" + hit.Role;
                    }
                }

                return hit;
            }
            catch
            {
                return null;
            }
        }

        private static string DeepFindText(IAccessible acc, int depth, ref int budget)
        {
            if (acc == null || depth > 3 || budget <= 0) return null;

            int count = 0;
            try { count = acc.accChildCount; } catch { return null; }
            if (count <= 0) return null;

            for (int i = 1; i <= count && budget > 0; i++)
            {
                budget--;

                object childObj = null;
                try { childObj = acc.get_accChild(i); } catch { childObj = null; }

                IAccessible c = childObj as IAccessible;
                if (c == null)
                {
                    // 简单子元素：直接用父接口按 child id 取值
                    int role = 0;
                    try { role = Convert.ToInt32(acc.get_accRole(i)); } catch { }
                    string name = "";
                    try { name = (acc.get_accName(i) ?? "").Trim(); } catch { }
                    if ((role == ROLE_SYSTEM_TEXT || role == ROLE_SYSTEM_STATICTEXT || role == ROLE_SYSTEM_LINK)
                        && name.Length > 0)
                        return name;
                    continue;
                }

                int cRole = 0;
                try { cRole = Convert.ToInt32(c.get_accRole(0)); } catch { }
                string cName = "";
                try { cName = (c.get_accName(0) ?? "").Trim(); } catch { }

                if ((cRole == ROLE_SYSTEM_TEXT || cRole == ROLE_SYSTEM_STATICTEXT || cRole == ROLE_SYSTEM_LINK)
                    && cName.Length > 0)
                    return cName;

                string deeper = DeepFindText(c, depth + 1, ref budget);
                if (!string.IsNullOrEmpty(deeper)) return deeper;
            }
            return null;
        }
    }
}
