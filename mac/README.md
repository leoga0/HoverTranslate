# 悬停翻译 macOS 版

鼠标停在屏幕上的文字处，约 0.34 秒后自动弹出译文卡片。不选中、不复制、不切换窗口。

## 构建

需要 macOS 11 以上，以及 Xcode Command Line Tools。

```bash
xcode-select --install          # 已装过可跳过
cd mac
bash build.sh
```

脚本会编译出 `mac/build/HoverTranslate.app`，并把应用图标、Info.plist、ad-hoc 签名一并处理好。

把 `HoverTranslate.app` 拖进「应用程序」文件夹，双击启动。

## 首次运行

菜单栏出现气泡图标后，第一次悬停不会有反应 —— 需要先授权：

系统设置 → 隐私与安全性 → 辅助功能 → 勾选 HoverTranslate

授权后回到程序，直接悬停即可。程序已经在跑，不需要重启。

如果菜单里点了「打开辅助功能设置」没反应，手动打开系统设置找一下这一项。

## 用法

鼠标停在网页、文档、聊天窗口的文字上不动，鼠标旁浮出译文卡片：上面灰色是原文，下面大字是译文。

- 按住卡片可以拖动
- 右键复制译文
- 点 × 关闭
- 鼠标移开取词位置一段距离后自动收起

## 快捷键

| 组合键 | 作用 |
| --- | --- |
| Control+Option+T | 立即翻译光标处文字；取不到则翻剪贴板 |
| Control+Option+D | 切换方向：自动识别 / 英译中 / 中译英 |
| Control+Option+H | 暂停或恢复悬停 |

全局快捷键同样依赖辅助功能权限。

## 菜单

悬停开关、翻译方向、深浅色、是否显示原文、打开辅助功能设置、快捷键说明、退出。

## 取词原理

用 macOS 的 Accessibility API：`AXUIElementCopyElementAtPosition` 拿到光标下的 UI 元素，再读它的 `AXValue` / `AXSelectedText` / `AXTitle`；读到的是容器就向下找子元素，还不行就沿父链向上找。

Safari、Chrome、Pages、Word、飞书、微信等实现了标准无障碍接口的应用都能取到。自绘界面、游戏画面、图片里的文字取不到，这时用 Control+Option+T 翻剪贴板内容。

## 翻译引擎

腾讯交互翻译（免费接口）→ 有道翻译 → MyMemory 依次降级，结果本地缓存。

## 日志

`~/Library/Logs/HoverTranslate/hover.log`

## 已知限制

- 只能同时运行一个实例（macOS 应用默认行为）
- 图片、扫描件 PDF、视频画面没有 OCR，取不到文字
- 多显示器且主副屏分辨率差异很大时，卡片定位可能偏差十几像素

## 未在真机验证的部分

这份 Swift 代码是在 Windows 上写的，没有经过 macOS 编译。

如果 `bash build.sh` 报错，把终端里的错误信息贴出来，或者开 issue，很快能修好。

翻译逻辑、取词逻辑与已实测通过的 Windows 版一一对应，主要风险在 AppKit 的布局与窗口定位细节。
