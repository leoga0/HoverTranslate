# Windows 版构建

不需要 Visual Studio，不需要 NuGet，只用 Windows 自带的 .NET Framework 编译器。

## 环境要求

- Windows 10 / 11
- .NET Framework 4.6 以上（系统自带）
- PowerShell 5.1 以上（系统自带）

## 构建

在仓库根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File windows\build.ps1
```

产物：`windows\dist\HoverTranslate.exe`（约 89 KB，单文件，无运行时依赖）。

脚本会自动：

1. 结束正在运行的旧版本，避免文件占用
2. 定位 `csc.exe` 与 WPF/UIAutomation 程序集路径
3. 按 `/codepage:65001` 编译 `src\*.cs`
4. 嵌入 `assets\app.ico`

## 源码结构

| 文件 | 职责 |
| --- | --- |
| `App.cs` | 入口、托盘、全局热键、悬停轮询、翻译调度 |
| `TextGrabber.cs` | UI Automation 取词（TextPattern 走反射，兼容被裁剪的系统） |
| `MsaaGrabber.cs` | MSAA 兜底取词，含子对象遍历 |
| `Translators.cs` | 文本整形、语种判断、三级翻译引擎与缓存 |
| `CardWindow.cs` | 无边框置顶浮窗、主题、定位、淡入淡出 |
| `Config.cs` | 配置读写（%APPDATA%，不可写时退回程序目录） |
| `Native.cs` | Win32 互操作、DPI、托盘图标绘制 |

## 调试

日志：`%APPDATA%\HoverTranslate\log.txt`

常见问题：程序被放到受策略管控的目录下运行时，进程会被降为低完整性，UIA/MSAA 跨进程访问被系统拒绝（日志里是"拒绝访问"），表现为悬停无反应。把 exe 移到普通目录即可。
