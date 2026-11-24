using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace HoverTranslate
{
    public enum AutoStartState
    {
        Off = 0,        // 没有注册启动项
        On = 1,         // 已注册、未被禁用、文件还在
        Disabled = 2,   // 注册了，但被任务管理器或安全软件禁用
        Broken = 3      // 注册了，但指向的 exe 已经不在了
    }

    /// <summary>
    /// 开机自启。
    /// 以前只往 HKCU\...\Run 写一个值，写进去就完事，出问题只能靠用户自己发现：
    /// 任务管理器里一禁用、安全软件一清、换目录一重装，注册表里的旧路径还在，
    /// 开关照样显示开着，开机却什么都没发生。
    /// 现在把三种情况分开认，并且给注册表被拦时留一条启动文件夹的退路。
    /// </summary>
    public static class AutoStart
    {
        public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        public const string RunName = "HoverTranslate";
        public const string BootArg = "--autostart";
        public const string ShortcutName = "HoverTranslate.lnk";

        /// <summary>
        /// 自检用：把注册表和启动文件夹换成内存。自检进程常常被降权，
        /// 真实注册表写不进去，逻辑就永远验不到，所以给一个纯内存的后端。
        /// </summary>
        internal static bool UseMemoryStoreForTest;

        private static string _memCmd;
        private static bool _memApproved = true;
        private static bool _memShortcut;

        /// <summary>自检里直接摆一个状态出来，比如指向已删除文件的启动项。</summary>
        internal static void MemoryStoreSetForTest(string cmd, bool approved, bool shortcut)
        {
            _memCmd = cmd;
            _memApproved = approved;
            _memShortcut = shortcut;
        }

        /// <summary>最近一次写入失败的原因，给诊断用。</summary>
        public static string LastError;

        private static string RunPath
        {
            get { return RunKeyPath; }
        }

        // ---------------- 基本信息 ----------------

        public static string ExePath()
        {
            try
            {
                string p = Process.GetCurrentProcess().MainModule.FileName;
                if (!string.IsNullOrEmpty(p)) return p;
            }
            catch { }
            try
            {
                Assembly asm = Assembly.GetEntryAssembly();
                if (asm != null && !string.IsNullOrEmpty(asm.Location)) return asm.Location;
            }
            catch { }
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HoverTranslate.exe");
        }

        /// <summary>注册表里那串原始命令，例如 "C:\...\HoverTranslate.exe" --autostart。</summary>
        public static string RegisteredCommand()
        {
            if (UseMemoryStoreForTest) return _memCmd;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunPath, false))
                {
                    if (k == null) return null;
                    return k.GetValue(RunName) as string;
                }
            }
            catch { return null; }
        }

        private static bool WriteCommand(string cmd)
        {
            if (UseMemoryStoreForTest) { _memCmd = cmd; return true; }
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunPath))
                {
                    if (k == null) { LastError = "打不开 Run 键"; return false; }
                    k.SetValue(RunName, cmd, RegistryValueKind.String);
                }
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        private static void DeleteCommand()
        {
            if (UseMemoryStoreForTest) { _memCmd = null; return; }
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunPath, true))
                {
                    if (k != null) k.DeleteValue(RunName, false);
                }
            }
            catch (Exception ex) { AppConfig.Log("autostart delete: " + ex.Message); }
        }

        /// <summary>从启动命令里取出 exe 路径，去掉引号和后面挂的参数。</summary>
        public static string ParseExe(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return null;
            cmd = cmd.Trim();
            if (cmd.StartsWith("\""))
            {
                int end = cmd.IndexOf('"', 1);
                if (end > 1) return cmd.Substring(1, end - 1);
                return cmd.Trim('"');
            }
            int sp = cmd.IndexOf(' ');
            return sp > 0 ? cmd.Substring(0, sp) : cmd;
        }

        public static string RegisteredExe()
        {
            return ParseExe(RegisteredCommand());
        }

        public static bool IsRegistered()
        {
            return !string.IsNullOrEmpty(RegisteredCommand());
        }

        /// <summary>
        /// 系统有没有把这个启动项禁用。
        /// StartupApproved\Run 里同名值是 12 字节，首字节偶数为启用（0x02），奇数为禁用（0x03/0x07）。
        /// 没有这个值也算启用——多数程序都没写过它。
        /// </summary>
        public static bool IsApproved()
        {
            if (UseMemoryStoreForTest) return _memApproved;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, false))
                {
                    if (k == null) return true;
                    byte[] v = k.GetValue(RunName) as byte[];
                    if (v == null || v.Length == 0) return true;
                    return (v[0] & 0x01) == 0;
                }
            }
            catch { return true; }
        }

        public static AutoStartState State()
        {
            if (!IsRegistered()) return AutoStartState.Off;
            if (!IsApproved()) return AutoStartState.Disabled;
            string exe = RegisteredExe();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return AutoStartState.Broken;
            return AutoStartState.On;
        }

        public static bool IsEnabled()
        {
            return State() == AutoStartState.On;
        }

        public static bool LaunchArgIsBoot(string[] args)
        {
            if (args == null) return false;
            foreach (string a in args)
            {
                if (string.Equals(a, BootArg, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // ---------------- 开关 ----------------

        /// <summary>清掉系统禁用标记，让启动项重新生效。返回是否成功。</summary>
        public static bool ClearDisabledFlag()
        {
            if (UseMemoryStoreForTest) { _memApproved = true; return true; }
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, true))
                {
                    if (k == null) return true;
                    if (k.GetValue(RunName) == null) return true;
                    byte[] ok = new byte[12];
                    ok[0] = 0x02;
                    k.SetValue(RunName, ok, RegistryValueKind.Binary);
                    return true;
                }
            }
            catch { return false; }
        }

        /// <summary>
        /// 注册开机自启。两条腿：注册表 Run 键为主；写不进去（被安全软件拦、权限受限）
        /// 就退到启动文件夹放一个快捷方式。
        /// </summary>
        public static bool Enable(out string note)
        {
            note = "";
            string exe = ExePath();
            string cmd = "\"" + exe + "\" " + BootArg;

            bool wrote = WriteCommand(cmd);
            if (!wrote) note = "注册表写入被拦：" + (LastError == null ? "未知原因" : LastError);

            ClearDisabledFlag();

            if (!IsRegistered())
            {
                if (CreateStartupShortcut(exe))
                {
                    note = "注册表写不进去，已改用启动文件夹快捷方式";
                }
                else if (note.Length == 0)
                {
                    note = "注册表和启动文件夹都没写成功";
                }
            }

            AutoStartState st = State();
            AppConfig.Log("autostart enable -> " + st + " cmd=" + cmd + (note.Length > 0 ? " note=" + note : ""));
            return st == AutoStartState.On || StartupShortcutExists();
        }

        public static void Disable()
        {
            DeleteCommand();
            if (!UseMemoryStoreForTest) RemoveStartupShortcut();
            AppConfig.Log("autostart disable -> " + State());
        }

        /// <summary>
        /// 每次启动把真实状态校准成配置里写的样子。
        /// 只修 Off 和 Broken：Disabled 是用户或系统主动禁用的，替用户改回去不合适。
        /// </summary>
        public static bool Ensure(bool wanted)
        {
            AutoStartState st = State();
            if (wanted)
            {
                if (st == AutoStartState.On) return false;
                if (st == AutoStartState.Disabled)
                {
                    AppConfig.Log("autostart: 启动项被系统禁用，等用户在菜单里重新打开");
                    return false;
                }
                string note;
                bool ok = Enable(out note);
                AppConfig.Log("autostart 自愈 wanted=true 原状态=" + st + " 结果=" + State() + " ok=" + ok);
                return true;
            }

            if (st != AutoStartState.Off || StartupShortcutExists())
            {
                Disable();
                return true;
            }
            return false;
        }

        // ---------------- 启动文件夹快捷方式 ----------------

        public static string StartupFolder()
        {
            try { return Environment.GetFolderPath(Environment.SpecialFolder.Startup); }
            catch { return null; }
        }

        public static string ShortcutPath()
        {
            string dir = StartupFolder();
            if (string.IsNullOrEmpty(dir)) return null;
            return Path.Combine(dir, ShortcutName);
        }

        public static bool StartupShortcutExists()
        {
            if (UseMemoryStoreForTest) return _memShortcut;
            try
            {
                string p = ShortcutPath();
                return p != null && File.Exists(p);
            }
            catch { return false; }
        }

        /// <summary>用 WScript.Shell 建 .lnk。反射调用，不额外引 COM 互操作程序集。</summary>
        public static bool CreateStartupShortcut(string exe)
        {
            if (UseMemoryStoreForTest) { _memShortcut = true; return true; }
            object shell = null;
            object sc = null;
            try
            {
                string lnk = ShortcutPath();
                if (string.IsNullOrEmpty(lnk)) return false;

                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return false;
                shell = Activator.CreateInstance(t);
                sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });

                Type st = sc.GetType();
                st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { exe });
                st.InvokeMember("Arguments", BindingFlags.SetProperty, null, sc, new object[] { BootArg });
                st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc,
                    new object[] { Path.GetDirectoryName(exe) });
                st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { exe + ",0" });
                st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "悬停翻译 · 开机自启" });
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                return File.Exists(lnk);
            }
            catch (Exception ex)
            {
                AppConfig.Log("startup shortcut: " + ex.Message);
                return false;
            }
            finally
            {
                if (sc != null) { try { Marshal.ReleaseComObject(sc); } catch { } }
                if (shell != null) { try { Marshal.ReleaseComObject(shell); } catch { } }
            }
        }

        public static void RemoveStartupShortcut()
        {
            if (UseMemoryStoreForTest) { _memShortcut = false; return; }
            try
            {
                string p = ShortcutPath();
                if (p != null && File.Exists(p)) File.Delete(p);
            }
            catch (Exception ex) { AppConfig.Log("startup shortcut del: " + ex.Message); }
        }

        // ---------------- 诊断 ----------------

        public static string StateText(AutoStartState st)
        {
            switch (st)
            {
                case AutoStartState.On: return "已开启";
                case AutoStartState.Disabled: return "已注册但被系统禁用";
                case AutoStartState.Broken: return "已注册但指错了文件";
                default: return "未开启";
            }
        }

        public static string Diag()
        {
            AutoStartState st = State();
            string exe = ExePath();
            string regExe = RegisteredExe();
            string lnk = ShortcutPath();

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("状态：" + StateText(st));
            sb.AppendLine("程序位置：" + exe + (File.Exists(exe) ? "" : "（文件不存在！）"));
            sb.AppendLine("注册表值：" + (RegisteredCommand() == null ? "（空）" : RegisteredCommand()));
            if (regExe != null && !string.Equals(regExe, exe, StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("注意：注册表指向的位置和现在运行的不是同一个文件");
            }
            sb.AppendLine("系统禁用标记：" + (IsApproved() ? "无" : "有（任务管理器或安全软件禁过）"));
            sb.AppendLine("启动文件夹快捷方式：" + (StartupShortcutExists() ? lnk : "无"));
            return sb.ToString();
        }
    }
}
