import Cocoa
import ApplicationServices

final class AppDelegate: NSObject, NSApplicationDelegate {

    private var statusItem: NSStatusItem!
    private var panel: CardPanel!
    private var timer: Timer?
    private let translator = Translator()
    private let config = Config.shared

    private var lastPos = NSPoint(x: -1, y: -1)
    private var stillSince = Date()
    private var lastTrigger = NSPoint(x: -99999, y: -99999)
    private var busy = false
    private var requestId = 0
    private var lastText = ""

    private var hoverMenuItem: NSMenuItem!
    private var dirItems: [NSMenuItem] = []
    private var sourceItem: NSMenuItem!
    private var darkItem: NSMenuItem!

    // MARK: - 生命周期

    func applicationDidFinishLaunching(_ notification: Notification) {
        Config.log("launch")

        panel = CardPanel()
        panel.cardAnchor = config.cardAnchor
        panel.darkTheme = config.darkTheme
        panel.fontSize = config.fontSize
        panel.showOriginal = config.showOriginal
        panel.appliesTheme()

        setupStatusItem()
        setupHotkeys()
        startTimer()

        if !Grabber.isTrusted() {
            Grabber.requestTrust()
            panel.showMessage("需要辅助功能权限", detail: "系统设置 → 隐私与安全性 → 辅助功能，勾选 HoverTranslate")
            DispatchQueue.main.asyncAfter(deadline: .now() + 2.2) {
                self.panel.hideCard()
            }
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        timer?.invalidate()
        Config.log("terminate")
    }

    // MARK: - 菜单栏

    private func setupStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
        if let button = statusItem.button {
            if let img = NSImage(systemSymbolName: "character.bubble", accessibilityDescription: "悬停翻译") {
                img.isTemplate = true
                button.image = img
                button.imagePosition = .imageOnly
            } else {
                button.title = "译"
                button.font = NSFont.boldSystemFont(ofSize: 14)
            }
            button.toolTip = "悬停翻译：鼠标停在文字上即出译文"
        }

        let menu = NSMenu()

        hoverMenuItem = NSMenuItem(title: "悬停翻译", action: #selector(toggleHover), keyEquivalent: "")
        hoverMenuItem.target = self
        hoverMenuItem.state = config.hoverEnabled ? .on : .off
        menu.addItem(hoverMenuItem)
        menu.addItem(NSMenuItem.separator())

        let targetMenu = NSMenuItem(title: "翻译目标", action: nil, keyEquivalent: "")
        let targetSub = NSMenu()
        let targetTitles = ["译成中文（只在非中文上翻译）", "译成英文（只在中文上翻译）", "双向都翻"]
        let targetKeys = ["zh", "en", "both"]
        for (idx, title) in targetTitles.enumerated() {
            let item = NSMenuItem(title: title, action: #selector(setTargetLang(_:)), keyEquivalent: "")
            item.target = self
            item.tag = idx
            item.state = config.targetLang == targetKeys[idx] ? .on : .off
            targetSub.addItem(item)
            dirItems.append(item)
        }
        targetMenu.submenu = targetSub
        menu.addItem(targetMenu)

        let posMenu = NSMenuItem(title: "卡片位置", action: nil, keyEquivalent: "")
        let posSub = NSMenu()
        let posTitles = ["鼠标左上方（不挡下文）", "鼠标右侧", "固定右下角"]
        for (idx, title) in posTitles.enumerated() {
            let item = NSMenuItem(title: title, action: #selector(setAnchor(_:)), keyEquivalent: "")
            item.target = self
            item.tag = idx
            item.state = config.cardAnchor == idx ? .on : .off
            posSub.addItem(item)
        }
        posMenu.submenu = posSub
        menu.addItem(posMenu)

        let themeMenu = NSMenuItem(title: "外观", action: nil, keyEquivalent: "")
        let themeSub = NSMenu()

        darkItem = NSMenuItem(title: "深色卡片", action: #selector(toggleTheme), keyEquivalent: "")
        darkItem.target = self
        darkItem.state = config.darkTheme ? .on : .off
        themeSub.addItem(darkItem)
        themeSub.addItem(NSMenuItem.separator())

        sourceItem = NSMenuItem(title: "显示原文", action: #selector(toggleSource), keyEquivalent: "")
        sourceItem.target = self
        sourceItem.state = config.showOriginal ? .on : .off
        themeSub.addItem(sourceItem)

        themeMenu.submenu = themeSub
        menu.addItem(themeMenu)

        menu.addItem(NSMenuItem.separator())

        let permItem = NSMenuItem(title: "打开辅助功能设置", action: #selector(openAccessibility), keyEquivalent: "")
        permItem.target = self
        menu.addItem(permItem)

        let helpItem = NSMenuItem(title: "快捷键说明", action: #selector(showHelp), keyEquivalent: "")
        helpItem.target = self
        menu.addItem(helpItem)

        menu.addItem(NSMenuItem.separator())

        let quitItem = NSMenuItem(title: "退出", action: #selector(quit), keyEquivalent: "q")
        quitItem.target = self
        menu.addItem(quitItem)

        statusItem.menu = menu
    }

    @objc private func toggleHover() {
        config.hoverEnabled = !config.hoverEnabled
        hoverMenuItem.state = config.hoverEnabled ? .on : .off
        if !config.hoverEnabled { panel.hideCard() }
    }

    @objc private func setTargetLang(_ sender: NSMenuItem) {
        let keys = ["zh", "en", "both"]
        config.targetLang = keys[max(0, min(2, sender.tag))]
        for item in dirItems {
            item.state = item.tag == sender.tag ? .on : .off
        }
    }

    @objc private func setAnchor(_ sender: NSMenuItem) {
        config.cardAnchor = sender.tag
        if let menu = sender.menu {
            for item in menu.items {
                item.state = item.tag == sender.tag ? .on : .off
            }
        }
    }

    @objc private func toggleTheme() {
        config.darkTheme = !config.darkTheme
        darkItem.state = config.darkTheme ? .on : .off
        panel.darkTheme = config.darkTheme
        panel.appliesTheme()
    }

    @objc private func toggleSource() {
        config.showOriginal = !config.showOriginal
        sourceItem.state = config.showOriginal ? .on : .off
        panel.showOriginal = config.showOriginal
    }

    @objc private func openAccessibility() {
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            NSWorkspace.shared.open(url)
        }
    }

    @objc private func showHelp() {
        let alert = NSAlert()
        alert.messageText = "悬停翻译"
        alert.informativeText = """
        鼠标停在任意文字上约 0.34 秒，自动弹出译文卡片。

        Control+Option+T   立即翻译光标处文字（取不到则翻剪贴板）
        Control+Option+D   切换翻译方向
        Control+Option+H   暂停或恢复悬停

        卡片：按住可拖动，右键复制译文，点 × 关闭。
        首次使用需要在「系统设置 → 隐私与安全性 → 辅助功能」里勾选本程序。
        """
        alert.addButton(withTitle: "知道了")
        alert.runModal()
    }

    @objc private func quit() {
        NSApplication.shared.terminate(nil)
    }

    // MARK: - 全局快捷键

    private func setupHotkeys() {
        _ = NSEvent.addGlobalMonitorForEvents(matching: .keyDown) { event in
            let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
            guard flags.contains(.control), flags.contains(.option) else { return }
            guard let key = event.charactersIgnoringModifiers?.lowercased() else { return }

            switch key {
            case "t":
                self.translateNow()
            case "d":
                self.cycleDirection()
            case "h":
                DispatchQueue.main.async { self.toggleHover() }
            default:
                break
            }
        }
    }

    private func cycleDirection() {
        let keys = ["zh", "en", "both"]
        let idx = keys.firstIndex(of: config.targetLang) ?? 0
        let next = (idx + 1) % keys.count
        config.targetLang = keys[next]
        DispatchQueue.main.async {
            for item in self.dirItems {
                item.state = item.tag == next ? .on : .off
            }
        }
    }

    private func translateNow() {
        let p = mouseQuartz()
        DispatchQueue.main.async {
            self.trigger(at: p, manual: true)
        }
    }

    // MARK: - 悬停轮询

    private func startTimer() {
        timer = Timer.scheduledTimer(withTimeInterval: 0.11, repeats: true) { _ in
            self.tick()
        }
        if let t = timer {
            RunLoop.main.add(t, forMode: .common)
        }
    }

    private func mouseQuartz() -> NSPoint {
        if let e = CGEvent(source: nil) {
            return e.location
        }
        return NSPoint.zero
    }

    private func tick() {
        let p = mouseQuartz()

        if p != lastPos {
            lastPos = p
            stillSince = Date()
            dismissIfFar(p)
            return
        }

        if panel.containsQuartzPoint(p) {
            panel.setDim(false)
            return
        }
        if !config.hoverEnabled { return }
        if busy { return }

        let still = Date().timeIntervalSince(stillSince)
        if still < config.hoverDelay { return }

        let dx = p.x - lastTrigger.x
        let dy = p.y - lastTrigger.y
        if dx * dx + dy * dy < 16 { return }

        trigger(at: p, manual: false)
    }

    private func dismissIfFar(_ p: NSPoint) {
        guard panel.isVisible else { return }
        if panel.containsQuartzPoint(p) { return }
        let dx = p.x - lastTrigger.x
        let dy = p.y - lastTrigger.y
        if dx * dx + dy * dy > 90 * 90 {
            panel.hideCard()
        }
    }

    // MARK: - 取词与翻译

    private func trigger(at p: NSPoint, manual: Bool) {
        busy = true
        lastTrigger = p
        requestId += 1
        let req = requestId
        let maxLen = config.maxTextLength

        DispatchQueue.global(qos: .userInitiated).async {
            var text = ""
            let result = Grabber.grab(x: p.x, y: p.y, maxLen: maxLen)
            if result.found, TextShape.worth(result.text) {
                text = result.text
            }
            let needClipboard = text.isEmpty && manual

            DispatchQueue.main.async {
                self.busy = false
                if req != self.requestId { return }

                var finalText = text
                if needClipboard,
                   let clip = NSPasteboard.general.string(forType: .string),
                   TextShape.worth(clip) {
                    finalText = TextShape.clean(clip, maxLen: maxLen)
                }
                self.handle(text: finalText, at: p, manual: manual)
            }
        }
    }

    /// 结果稳定后把卡片压到背景；鼠标移上去会恢复。
    private func scheduleDim() {
        DispatchQueue.main.asyncAfter(deadline: .now() + 2.1) {
            guard self.panel.isVisible, self.panel.hasResult else { return }
            let p = self.mouseQuartz()
            if !self.panel.containsQuartzPoint(p) {
                self.panel.setDim(true)
            }
        }
    }

    /// 整段都已经是目标语言时不翻译；手动快捷键不受限制。
    private func shouldTranslate(_ text: String, manual: Bool) -> Bool {
        if manual { return true }
        // 命中技术术语就值得弹，哪怕整段都是中文、哪怕不需要翻译
        if !Terminology.match(text, max: 1).isEmpty { return true }
        if config.targetLang == "both" { return true }
        return LangSplitter.countTranslatable(text, targetIsCjk: config.targetLang == "zh") > 0
    }

    private func targetLabel() -> String {
        if config.targetLang == "both" { return "双向" }
        return config.targetLang == "zh" ? "译成中文" : "译成英文"
    }

    private func handle(text: String, at p: NSPoint, manual: Bool) {
        if text.isEmpty {
            if manual {
                panel.showMessage("光标下没有取到文字", detail: "把鼠标放到文字上，或先选中文字再按 Control+Option+T")
                DispatchQueue.main.asyncAfter(deadline: .now() + 1.6) { self.panel.hideCard() }
            }
            return
        }

        // 目标语言过滤：整段已经是目标语言时不打扰，顺手把旧卡片收掉
        if !shouldTranslate(text, manual: manual) {
            if !manual { panel.hideCard() }
            return
        }

        if text == lastText && panel.isVisible { return }
        lastText = text

        panel.darkTheme = config.darkTheme
        panel.fontSize = config.fontSize
        panel.showOriginal = config.showOriginal
        panel.cardAnchor = config.cardAnchor
        panel.appliesTheme()
        panel.showLoading(source: text, pair: targetLabel(), quartzX: p.x, quartzY: p.y)

        let req = requestId
        let target = config.targetLang

        DispatchQueue.global(qos: .userInitiated).async {
            let m = self.translator.translateForTarget(text, targetLang: target)
            DispatchQueue.main.async {
                if req != self.requestId { return }
                if !m.shouldShow {
                    self.panel.hideCard()
                    return
                }
                var r = TranslationResult()
                r.success = m.success
                r.source = m.source
                r.target = m.target
                r.from = m.from
                r.to = m.to
                r.engine = m.engine
                r.error = m.error
                self.panel.showResult(r)
                self.panel.showTerms(m.terms)
                self.scheduleDim()
            }
        }
    }
}

// MARK: - 入口

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
app.setActivationPolicy(.accessory)
app.run()
