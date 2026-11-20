import Cocoa

/// 译文浮层。
/// 设计目标是「存在感足够低」：小尺寸、半透明、默认只显示译文、闲置时自动退到背景。
/// 全部采用 frame 布局，尺寸随内容确定性变化。
final class CardPanel: NSPanel {

    static let cardWidth: CGFloat = 340

    private let padX: CGFloat = 11
    private let padTop: CGFloat = 7
    private let padBottom: CGFloat = 8
    private let gap: CGFloat = 5

    private let backdrop = NSVisualEffectView()
    private let headerLabel = NSTextField(labelWithString: "")
    private let sourceField = NSTextField(labelWithString: "")
    private let targetField = NSTextField(labelWithString: "")
    private let footerField = NSTextField(labelWithString: "")
    private let closeButton = NSButton()
    private let divider = NSView()
    private let termDivider = NSView()
    private var termRows: [(NSTextField, NSTextField)] = []

    private var anchorQuartz = NSPoint.zero
    private(set) var translated = ""
    private(set) var hasResult = false
    private var dimmed = false

    var darkTheme = true
    var cardAnchor = 0
    var fontSize: CGFloat = 14
    var showOriginal = false

    init() {
        super.init(contentRect: NSRect(x: 0, y: 0, width: CardPanel.cardWidth, height: 100),
                   styleMask: [.borderless, .nonactivatingPanel],
                   backing: .buffered, defer: false)

        isFloatingPanel = true
        level = .floating
        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
        isMovableByWindowBackground = true
        hidesOnDeactivate = false
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient]
        alphaValue = 0

        setupViews()
        appliesTheme()
    }

    override var canBecomeKey: Bool { return false }
    override var canBecomeMain: Bool { return false }

    // MARK: - 视图

    private func setupViews() {
        let container = NSView(frame: NSRect(x: 0, y: 0, width: CardPanel.cardWidth, height: 100))
        container.autoresizingMask = [.width, .height]
        contentView = container

        backdrop.frame = container.bounds
        backdrop.autoresizingMask = [.width, .height]
        backdrop.blendingMode = .behindWindow
        backdrop.state = .active
        backdrop.material = .hudWindow
        backdrop.wantsLayer = true
        backdrop.layer?.cornerRadius = 8
        backdrop.layer?.masksToBounds = true
        container.addSubview(backdrop)

        headerLabel.font = NSFont.systemFont(ofSize: 10, weight: .medium)
        headerLabel.maximumNumberOfLines = 1
        headerLabel.lineBreakMode = .byTruncatingTail
        container.addSubview(headerLabel)

        closeButton.title = "×"
        closeButton.isBordered = false
        closeButton.font = NSFont.systemFont(ofSize: 12)
        closeButton.target = self
        closeButton.action = #selector(closeTapped)
        container.addSubview(closeButton)

        divider.wantsLayer = true
        container.addSubview(divider)

        sourceField.maximumNumberOfLines = 3
        sourceField.lineBreakMode = .byTruncatingTail
        sourceField.cell?.wraps = true
        container.addSubview(sourceField)

        targetField.maximumNumberOfLines = 0
        targetField.lineBreakMode = .byWordWrapping
        targetField.cell?.wraps = true
        container.addSubview(targetField)

        footerField.maximumNumberOfLines = 1
        footerField.lineBreakMode = .byTruncatingTail
        footerField.isHidden = true
        container.addSubview(footerField)

        termDivider.wantsLayer = true
        termDivider.isHidden = true
        container.addSubview(termDivider)
    }

    // MARK: - 主题

    func appliesTheme() {
        backdrop.material = darkTheme ? .hudWindow : .popover

        headerLabel.textColor = darkTheme
            ? NSColor(calibratedRed: 0.62, green: 0.76, blue: 0.96, alpha: 0.55)
            : NSColor(calibratedRed: 0.24, green: 0.43, blue: 0.74, alpha: 0.75)
        headerLabel.font = NSFont.systemFont(ofSize: 10, weight: .medium)

        sourceField.textColor = darkTheme
            ? NSColor(calibratedWhite: 0.78, alpha: 0.55)
            : NSColor(calibratedWhite: 0.42, alpha: 0.8)
        sourceField.font = NSFont.systemFont(ofSize: max(10, fontSize - 2.5))

        targetField.textColor = darkTheme
            ? NSColor(calibratedWhite: 1.0, alpha: 0.94)
            : NSColor(calibratedWhite: 0.13, alpha: 1)
        targetField.font = NSFont.systemFont(ofSize: fontSize)

        footerField.textColor = darkTheme
            ? NSColor(calibratedWhite: 0.70, alpha: 0.55)
            : NSColor(calibratedWhite: 0.48, alpha: 0.8)
        footerField.font = NSFont.systemFont(ofSize: 9.5)

        divider.layer?.backgroundColor = (darkTheme
            ? NSColor(calibratedWhite: 1.0, alpha: 0.08)
            : NSColor(calibratedWhite: 0.0, alpha: 0.07)).cgColor
        termDivider.layer?.backgroundColor = (darkTheme
            ? NSColor(calibratedWhite: 1.0, alpha: 0.07)
            : NSColor(calibratedWhite: 0.0, alpha: 0.06)).cgColor

        closeButton.contentTintColor = darkTheme
            ? NSColor(calibratedWhite: 1.0, alpha: 0.22)
            : NSColor(calibratedWhite: 0.0, alpha: 0.25)
    }

    // MARK: - 内容

    func showLoading(source: String, pair: String, quartzX: CGFloat, quartzY: CGFloat) {
        anchorQuartz = NSPoint(x: quartzX, y: quartzY)
        translated = ""
        hasResult = false
        dimmed = false
        headerLabel.stringValue = pair
        sourceField.stringValue = TextShape.clean(source, maxLen: 400)
        sourceField.isHidden = !showOriginal || sourceField.stringValue.isEmpty
        targetField.stringValue = "翻译中…"
        targetField.textColor = darkTheme
            ? NSColor(calibratedWhite: 0.82, alpha: 0.6)
            : NSColor(calibratedWhite: 0.35, alpha: 0.8)
        footerField.stringValue = ""
        footerField.isHidden = true
        showTerms([])
        present()
    }

    func showResult(_ r: TranslationResult) {
        hasResult = true

        if r.success {
            translated = r.target
            headerLabel.stringValue = compactPair(r)
            sourceField.stringValue = TextShape.clean(r.source, maxLen: 400)
            sourceField.isHidden = !showOriginal || sourceField.stringValue.isEmpty
            targetField.stringValue = r.target
            targetField.textColor = darkTheme
                ? NSColor(calibratedWhite: 1.0, alpha: 0.94)
                : NSColor(calibratedWhite: 0.13, alpha: 1)
            footerField.stringValue = ""
            footerField.isHidden = true
        } else {
            translated = ""
            sourceField.isHidden = true
            targetField.stringValue = "翻译失败"
            targetField.textColor = NSColor(calibratedRed: 1, green: 0.52, blue: 0.48, alpha: 1)
            footerField.stringValue = r.error
            footerField.isHidden = r.error.isEmpty
        }
        present()
    }

    func showMessage(_ message: String, detail: String) {
        hasResult = false
        dimmed = false
        headerLabel.stringValue = ""
        sourceField.stringValue = ""
        sourceField.isHidden = true
        translated = ""
        targetField.stringValue = message
        targetField.textColor = darkTheme
            ? NSColor(calibratedWhite: 1.0, alpha: 0.94)
            : NSColor(calibratedWhite: 0.13, alpha: 1)
        footerField.stringValue = detail
        footerField.isHidden = detail.isEmpty
        present()
    }

    /// 在译文下方补术语解释：一句话讲清楚，再打个比方。
    func showTerms(_ terms: [Terminology.Term]) {
        for pair in termRows {
            pair.0.removeFromSuperview()
            pair.1.removeFromSuperview()
        }
        termRows.removeAll()

        guard !terms.isEmpty, let container = contentView else {
            termDivider.isHidden = true
            return
        }

        let contentW = CardPanel.cardWidth - padX * 2
        var shown = 0

        for term in terms {
            if shown >= 2 { break }

            let head = NSTextField(labelWithString: term.c.isEmpty ? term.n : "\(term.n) · \(term.c)")
            head.font = NSFont.systemFont(ofSize: 9.5, weight: .semibold)
            head.textColor = darkTheme
                ? NSColor(calibratedRed: 0.59, green: 0.75, blue: 1.0, alpha: shown == 0 ? 0.85 : 0.6)
                : NSColor(calibratedRed: 0.16, green: 0.35, blue: 0.67, alpha: shown == 0 ? 0.95 : 0.7)
            head.maximumNumberOfLines = 1
            container.addSubview(head)

            var bodyText = term.d
            if !term.x.isEmpty { bodyText += "\n" + term.x }

            let body = NSTextField(wrappingLabelWithString: bodyText)
            body.font = NSFont.systemFont(ofSize: 11)
            body.preferredMaxLayoutWidth = contentW
            body.maximumNumberOfLines = 0
            body.textColor = darkTheme
                ? NSColor(calibratedWhite: 0.89, alpha: shown == 0 ? 0.9 : 0.65)
                : NSColor(calibratedWhite: 0.22, alpha: shown == 0 ? 0.95 : 0.75)
            container.addSubview(body)

            termRows.append((head, body))
            shown += 1
        }

        termDivider.isHidden = termRows.isEmpty
    }

    private func compactPair(_ r: TranslationResult) -> String {
        var pair = Lang.displayPair(r.from, r.to)
        if !r.engine.isEmpty {
            var engine = r.engine
            if engine.hasSuffix("翻译") { engine = String(engine.dropLast(2)) }
            if engine.count > 6 { engine = String(engine.prefix(6)) }
            pair += " · " + engine
        }
        return pair
    }

    // MARK: - 布局

    private func textHeight(_ text: String, font: NSFont, width: CGFloat, maxLines: Int) -> CGFloat {
        if text.isEmpty { return 0 }
        let attr = NSAttributedString(string: text, attributes: [.font: font])
        let rect = attr.boundingRect(with: NSSize(width: width, height: 20000),
                                     options: [.usesLineFragmentOrigin, .usesFontLeading])
        var h = ceil(rect.height) + 1
        if maxLines > 0 {
            let lineH = ceil(font.ascender - font.descender + font.leading)
            h = min(h, lineH * CGFloat(maxLines))
        }
        return h
    }

    private func present() {
        let contentW = CardPanel.cardWidth - padX * 2
        let headerH: CGFloat = 13
        let dividerH: CGFloat = 1
        let footerVisible = !footerField.isHidden

        let sourceH = sourceField.isHidden ? 0 : textHeight(
            sourceField.stringValue,
            font: sourceField.font ?? NSFont.systemFont(ofSize: 12),
            width: contentW, maxLines: 3)
        let targetH = max(15, textHeight(
            targetField.stringValue,
            font: targetField.font ?? NSFont.systemFont(ofSize: fontSize),
            width: contentW, maxLines: 0))
        let footerH = footerVisible ? textHeight(
            footerField.stringValue,
            font: footerField.font ?? NSFont.systemFont(ofSize: 9.5),
            width: contentW, maxLines: 1) : 0

        var termTotal: CGFloat = 0
        if !termRows.isEmpty {
            termTotal = 1 + 5
            for (i, pair) in termRows.enumerated() {
                if i > 0 { termTotal += 6 }
                termTotal += 13 + 1
                termTotal += textHeight(pair.1.stringValue,
                                        font: pair.1.font ?? NSFont.systemFont(ofSize: 11),
                                        width: contentW, maxLines: 0)
            }
        }

        var total = padTop + headerH + gap + dividerH + gap
        if sourceH > 0 { total += sourceH + 5 }
        total += targetH
        total += termTotal > 0 ? termTotal + 5 : 0
        if footerH > 0 { total += footerH + 5 }
        total += padBottom

        let width = CardPanel.cardWidth
        let height = max(52, total)
        setContentSize(NSSize(width: width, height: height))
        guard let container = contentView else { return }
        container.frame = NSRect(x: 0, y: 0, width: width, height: height)
        backdrop.frame = container.bounds

        var top = height - padTop

        headerLabel.frame = NSRect(x: padX, y: top - headerH, width: contentW - 18, height: headerH)
        closeButton.frame = NSRect(x: width - padX - 14, y: top - headerH, width: 14, height: 13)
        top -= headerH + gap

        divider.frame = NSRect(x: padX, y: top - dividerH, width: contentW, height: dividerH)
        top -= dividerH + gap

        if sourceH > 0 {
            sourceField.frame = NSRect(x: padX, y: top - sourceH, width: contentW, height: sourceH)
            top -= sourceH + 5
        }

        targetField.frame = NSRect(x: padX, y: top - targetH, width: contentW, height: targetH)
        top -= targetH + 5

        if !termRows.isEmpty {
            termDivider.frame = NSRect(x: padX, y: top - 1, width: contentW, height: 1)
            top -= 1 + 5

            for (i, pair) in termRows.enumerated() {
                if i > 0 { top -= 6 }
                pair.0.frame = NSRect(x: padX, y: top - 13, width: contentW, height: 13)
                top -= 13 + 1
                let bh = textHeight(pair.1.stringValue,
                                    font: pair.1.font ?? NSFont.systemFont(ofSize: 11),
                                    width: contentW, maxLines: 0)
                pair.1.frame = NSRect(x: padX, y: top - bh, width: contentW, height: bh)
                top -= bh
            }
        }

        if footerH > 0 {
            footerField.frame = NSRect(x: padX, y: top - footerH, width: contentW, height: footerH)
        }

        reposition()
        orderFrontRegardless()
        dimmed = false
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.12
            self.animator().alphaValue = 1
        }, completionHandler: nil)
    }

    // MARK: - 定位

    /// 卡片定位。默认放鼠标左上方——那里是已经读过的内容，不会挡住后面和下面的句子。
    /// 放不下时按 右上 → 左下 → 右下 的顺序找位置；也可以固定到屏幕右下角。
    func reposition() {
        guard let first = NSScreen.screens.first else { return }
        let size = frame.size
        let mainHeight = first.frame.height
        let cocoaY = mainHeight - anchorQuartz.y
        let margin: CGFloat = 8
        let gap: CGFloat = 16

        var x: CGFloat = 0
        var y: CGFloat = 0

        let probe = NSPoint(x: anchorQuartz.x, y: cocoaY)
        let visible = (screenContaining(probe) ?? first).visibleFrame

        if cardAnchor == 2 {
            x = visible.maxX - size.width - 16
            y = visible.minY + 16
        } else if cardAnchor == 1 {
            x = anchorQuartz.x + gap + 6
            y = cocoaY - size.height / 2
            if x + size.width > visible.maxX - margin {
                x = anchorQuartz.x - size.width - gap - 6
            }
        } else {
            let leftX = anchorQuartz.x - size.width - gap
            let rightX = anchorQuartz.x + gap
            let upY = cocoaY + gap
            let downY = cocoaY - size.height - gap

            x = leftX
            y = upY

            if x < visible.minX + margin { x = rightX }
            if x + size.width > visible.maxX - margin { x = leftX }
            if y + size.height > visible.maxY - margin { y = downY }
            if y < visible.minY + margin { y = upY }

            if x < visible.minX + margin { x = visible.minX + margin }
            if x + size.width > visible.maxX - margin { x = visible.maxX - size.width - margin }
            if y < visible.minY + margin { y = visible.minY + margin }
        }

        setFrameOrigin(NSPoint(x: x, y: y))
    }

    private func screenContaining(_ p: NSPoint) -> NSScreen? {
        for s in NSScreen.screens where s.frame.contains(p) {
            return s
        }
        return NSScreen.main
    }

    // MARK: - 显隐

    /// 闲置时压到接近背景，鼠标移上去恢复。
    func setDim(_ on: Bool) {
        guard isVisible else { return }
        if dimmed == on { return }
        dimmed = on
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = on ? 0.32 : 0.14
            self.animator().alphaValue = on ? 0.42 : 1.0
        }, completionHandler: nil)
    }

    func hideCard(animated: Bool = true) {
        guard isVisible else { return }
        hasResult = false
        dimmed = false
        if animated {
            NSAnimationContext.runAnimationGroup({ ctx in
                ctx.duration = 0.14
                self.animator().alphaValue = 0
            }, completionHandler: {
                self.orderOut(nil)
            })
        } else {
            alphaValue = 0
            orderOut(nil)
        }
    }

    func containsQuartzPoint(_ p: NSPoint) -> Bool {
        guard isVisible, alphaValue > 0.05 else { return false }
        let mainHeight = NSScreen.screens.first?.frame.height ?? 1080
        let cocoaY = mainHeight - p.y
        return frame.contains(NSPoint(x: p.x, y: cocoaY))
    }

    // MARK: - 交互

    @objc private func closeTapped() {
        hideCard()
    }

    override func rightMouseUp(with event: NSEvent) {
        let text = translated.isEmpty ? targetField.stringValue : translated
        guard !text.isEmpty else { return }
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.setString(text, forType: .string)
    }
}
