# 悬停翻译 HoverTranslate

鼠标停在屏幕上的任何文字处，0.3 秒后自动弹出译文卡片。不选中、不复制、不切换窗口。

Windows 与 macOS 双平台，中英双向翻译。

![译文卡片](docs/card.png)

![Build macOS](https://github.com/leoga0/HoverTranslate/actions/workflows/build-macos.yml/badge.svg)

## 它解决什么问题

读英文文档、看英文网页、处理外文合同时，传统做法是选中文字、复制、切到翻译网站、粘贴、切回来。一次两次还行，一天几十次就是纯浪费。

这个工具把整套动作压成"把鼠标停上去"。

## 特性

- 悬停取词：鼠标静止约 0.4 秒自动翻译光标下的文本，移开即收起
- 双向翻译：自动识别语种，也可固定为英译中或中译英
- 按需触发：设成「译成中文」后，鼠标停在中文上完全不响应；想看中文翻英文就切「译成英文」。也可以选「双向都翻」
- 混合语言只翻一半：一句话里中英混排时，只翻不属于目标语言的部分，另一半原样保留
- 选中即解释：想知道某个词什么意思，选中它（或整段话）停半秒，卡片自己出来——一页说明里全是陌生名词时，这比把鼠标精准停在某个词上更好用
- 术语就地解释：内置一百六十多条技术术语（API、幂等、熔断、Docker、Kubernetes、索引、灰度发布…），碰到就先用一句话说清是什么，再用生活里的例子打个比方，不需要先懂行话
- 低干扰视觉：小卡片、半透明、默认只显示译文；结果稳定 2 秒后自动淡到背景，鼠标移上去恢复
- 位置可避让：卡片默认落在鼠标左上方（已经读过的区域），不挡后面和下面的句子；也可切右侧或固定右下角
- 不打扰：不抢焦点、不打断输入，鼠标掠过按钮菜单不会乱弹
- 内容型取词：只翻正文，鼠标掠过按钮、菜单、标题栏不会乱弹
- 三级引擎降级：腾讯交互翻译 → 有道 → MyMemory，主引擎响应 0.2-0.4 秒
- 本地缓存：同一段文字重复悬停不重复请求
- 免安装：Windows 单文件 exe，macOS 单个 .app

## 下载与安装

全部产物在 [Releases](https://github.com/leoga0/HoverTranslate/releases) 页面。

| 平台 | 文件 | 安装方式 |
| --- | --- | --- |
| Windows 10 / 11 | `HoverTranslate.exe` | 放到任意普通目录，双击运行 |
| macOS 11+ | `HoverTranslate-macos.zip` | 解压后把 `HoverTranslate.app` 拖进「应用程序」 |

### Windows

任务栏右下角出现蓝色「译」图标即已启动，程序常驻后台，没有主窗口。

> 不要放在受策略管控的目录（例如某些沙箱工作区）下运行 —— 系统会把进程降为低完整性，此时读不到其他程序的界面文字。

### macOS

应用是 ad-hoc 签名的，从网络下载后第一次打开会被 Gatekeeper 拦一下。两种处理方式：

- 在「应用程序」里右键点 `HoverTranslate.app` → 打开 → 再点「打开」
- 或者终端执行 `xattr -cr /Applications/HoverTranslate.app`

首次运行还必须在「系统设置 → 隐私与安全性 → 辅助功能」里勾选 HoverTranslate，否则取不到文字。

### 从源码构建

Windows 不需要 Visual Studio，只用系统自带的 .NET Framework 编译器：

```powershell
powershell -ExecutionPolicy Bypass -File windows\build.ps1
```

macOS 需要 Xcode Command Line Tools：

```bash
xcode-select --install
cd mac && bash build.sh
```

细节见 [`windows/README.md`](windows/README.md) 与 [`mac/README.md`](mac/README.md)。

## 用法

鼠标停在文字上不动，旁边浮出卡片：灰色小字是原文，大字是译文。

- 按住卡片可拖动
- 右键复制译文
- 点 × 关闭
- 鼠标移开取词位置一段距离后自动收起

### 快捷键

| 功能 | Windows | macOS |
| --- | --- | --- |
| 立即翻译光标处文字（取不到则翻剪贴板） | Ctrl+Alt+T | Control+Option+T |
| 切换翻译目标（中文 / 英文 / 双向） | Ctrl+Alt+D | Control+Option+D |
| 翻译或解释选中的文字 | Ctrl+Alt+T | Control+Option+T |
| 暂停 / 恢复悬停 | Ctrl+Alt+H | Control+Option+H |

目标顺序：译成中文 → 译成英文 → 双向都翻 → 循环。

### 托盘 / 菜单栏

悬停开关、翻译目标、行业词库、卡片位置、深浅色、字号、是否显示原文、低调样式、闲置自动变淡、选中即解释、开机自启（Windows）、辅助功能设置（macOS）、退出。

「翻译目标」决定什么时候才翻译：译成中文时，中文文本直接跳过，不弹卡片；译成英文时反过来。选「双向都翻」回到无差别翻译。
「卡片位置」默认鼠标左上方（已读区域），避免挡住后面和下面的句子；也可选鼠标右侧或固定右下角。

「低调样式」默认开启：卡片更透明、无重阴影。关掉会切回对比度高一些的标准样式。
「闲置自动变淡」默认开启：结果显示 2 秒后把卡片压到 42% 透明度，鼠标移上去恢复。
「显示原文」默认关闭：只留译文，卡片更小；需要对照原文时在菜单里打开。

## 工作原理

### Windows

取词走两条通道：

1. **UI Automation**：`AutomationElement.FromPoint` 拿到光标下的元素，读 `Name` / `Value` / `TextPattern`，容器节点向下找包含光标的文本子元素
2. **MSAA**：UIA 在 Chromium 内核浏览器上会走到 MSAA 代理并被系统拒绝，此时改用 `AccessibleObjectFromPoint` 取 `IAccessible`，必要时遍历子对象

文本整形后送翻译引擎，结果显示在无边框置顶浮窗（`WS_EX_NOACTIVATE`，不抢焦点）。

### macOS

用 Accessibility API：`AXUIElementCopyElementAtPosition` 定位光标下的元素，读 `AXValue` / `AXSelectedText` / `AXTitle` / `AXDescription`；读到容器就向下找子元素，仍为空则沿父链向上找。浮窗用 `NSPanel`（`.nonactivatingPanel`）加 frame 布局实现。

## 术语库

内置 450 条离线条目：175 条技术术语，覆盖编程基础、Web、数据库、架构、运维/云、版本协作、安全、前端、AI、移动端；275 条行业术语，覆盖教育培训、电商运营、金融财税、法律合规、医疗健康、制造工业、汽车、房地产建筑、人力资源、互联网职场黑话、市场营销、物流供应链、餐饮零售。

行业词库目前只在 Windows 版提供，在托盘菜单里切换：选中某个行业后，该行业的行话优先命中，其他术语照常参与匹配、只是排在后面，选错行业也不会解释不出来。默认「自动（全部行业）」不偏向任何行业。

自建词库：菜单底部可「新增行业 / 岗位」「给当前行业加词条」，内容存在配置目录的 user-industries.json 与 user-terms.json，也可直接编辑文件。菜单里加的立即生效，改文件需重启。

命中后卡片下方会多出两段：

```
幂等 · 架构
同一个操作执行一次和执行多次，结果完全一样，重试不会出错。
像按电梯按钮，按一下和按十下都只来一趟。
```

- 术语库离线内嵌，不发额外请求
- 一条文本里命中多个术语时，选中行业优先，其次专有名词（Docker 会比"打包"先讲），最多显示两条
- 整段就是一个术语（比如只悬停 "API"）时，即使不需要翻译也会把解释显示出来
- 译文与原文相同但命中术语时，卡片照样弹，因为解释本身有价值

## 翻译引擎

| 顺序 | 引擎 | 说明 |
| --- | --- | --- |
| 1 | 腾讯交互翻译 | 免费接口，中英质量稳定，实测 0.2-0.4 秒 |
| 2 | 有道翻译 | 备用 |
| 3 | MyMemory | 兜底 |

三个都是公开免费接口，不需要 API Key。请求失败自动降级，全部失败时卡片显示错误原因。

## 目录结构

```
HoverTranslate/
├── windows/           Windows 版（C# / WPF，csc 直接编译，无第三方依赖）
│   ├── src/
│   ├── assets/
│   └── build.ps1
├── mac/               macOS 版（Swift / AppKit）
│   ├── Sources/
│   ├── Info.plist
│   ├── icon.png
│   └── build.sh
└── docs/
```

## 已知限制

- 图片里的文字、扫描版 PDF、视频画面需要 OCR，当前版本不含
- 以管理员权限运行的软件：普通权限启动的翻译器读不到它的界面
- 全屏独占的游戏取不到
- macOS 版多显示器且分辨率差异极大时，卡片定位可能偏差十几像素

取不到文字时，选中文字后按快捷键，或先复制再按快捷键。

## 反馈

开 issue 就行，附上系统版本和复现步骤。macOS 版由 GitHub Actions 在 macOS 上编译验证，运行行为仍需更多真机反馈。

## License

MIT
