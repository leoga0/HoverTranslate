import Cocoa

/// 译文浮窗。
/// 全部采用 frame 布局而非 Auto Layout：窗口尺寸随内容变化，用确定性计算更稳。
final class CardPanel: NSPanel {

    static let cardWidth: CGFloat = 380

    private let padX: CGFloat = 14
    private let padTop: CGFloat = 10
    private let padBottom: CGFloat = 11
    private let gap: CGFloat = 6

    private let backdrop = NSVisualEffectView()
    private let headerLabel = NSTextField(labelWithString: "")
    private let sourceField = NSTextField(labelWithString: "")
    private let targetField = NSTextField(labelWithString: "")
    private let footerField = NSTextField(labelWithString: "")
    private let closeButton = NSButton()
    private let divider = NSView()

    private var anchorQuartz = NSPoint.zero
    private(set) var translated = ""

    var darkTheme = true
    var fontSize: CGFloat = 15
    var showOriginal = true

    init() {
        super.init(contentRect: NSRect(x: 0, y: 0, width: CardPanel.cardWidth, height: 120),
                   styleMask: [.borderless, .nonactivatingPanel],
                   backing: .buffered, defer: false)

        isFloatingPanel = true
        level = .floating
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        isMovableByWindowBackground = true
        hidesOnDeactivate = false
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient]
        alphaValue = 0

        setupViews()
        appliesTheme()
    }

    override var canBecomeKey: Bool { return false }
    override var canBecomeMain: Bool { return false }

    // MARK: - 视图搭建

    private func setupViews() {
        let container = NSView(frame: NSRect(x: 0, y: 0, width: CardPanel.cardWidth, height: 120))
        container.autoresizingMask = [.width, .height]
        contentView = container

        backdrop.frame = container.bounds
        backdrop.autoresizingMask = [.width, .height]
        backdrop.blendingMode = .behindWindow
        backdrop.state = .active
        backdrop.wantsLayer = true
        backdrop.layer?.cornerRadius = 10
        backdrop.layer?.masksToBounds = true
        container.addSubview(backdrop)

        headerLabel.font = NSFont.systemFont(ofSize: 11, weight: .medium)
        headerLabel.maximumNumberOfLines = 1
        headerLabel.lineBreakMode = .byTruncatingTail
        container.addSubview(headerLabel)

        closeButton.title = "×"
        closeButton.isBordered = false
        closeButton.font = NSFont.systemFont(ofSize: 14)
        closeButton.contentTintColor = NSColor(calibratedWhite: 0.68, alpha: 1)
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
        container.addSubview(footerField)
    }

    // MARK: - 主题

    func appliesTheme() {
        backdrop.material = darkTheme ? .hudWindow : .popover

        headerLabel.textColor = darkTheme
            ? NSColor(calibratedRed: 0.62, green: 0.78, blue: 1.0, alpha: 1)
            : NSColor(calibratedRed: 0.11, green: 0.39, blue: 0.82, alpha: 1)
        headerLabel.font = NSFont.systemFont(ofSize: 11, weight: .medium)

        sourceField.textColor = darkTheme
            ? NSColor(calibratedWhite: 0.80, alpha: 1)
            : NSColor(calibratedWhite: 0.40, alpha: 1)
        sourceField.font = NSFont.systemFont(ofSize: max(10, fontSize - 3))

        targetField.textColor = darkTheme ? .white : NSColor(calibratedWhite: 0.12, alpha: 1)
        targetField.font = NSFont.systemFont(ofSize: fontSize)

        footerField.textColor = darkTheme
            ? NSColor(calibratedWhite: 0.60, alpha: 1)
            : NSColor(calibratedWhite: 0.50, alpha: 1)
        footerField.font = NSFont.systemFont(ofSize: 10)

        divider.layer?.backgroundColor = (darkTheme
            ? NSColor(calibratedWhite: 1.0, alpha: 0.12)
            : NSColor(calibratedWhite: 0.0, alpha: 0.10)).cgColor

        closeButton.contentTintColor = darkTheme
            ? NSColor(calibratedWhite: 0.70, alpha: 1)
            : NSColor(calibratedWhite: 0.35, alpha: 1)
    }

    // MARK: - 内容接口

    func showLoading(source: String, pair: String, quartzX: CGFloat, quartzY: CGFloat) {
        anchorQuartz = NSPoint(x: quartzX, y: quartzY)
        translated = ""
        headerLabel.stringValue = pair
        sourceField.stringValue = TextShape.clean(source, maxLen: 400)
        sourceField.isHidden = !showOriginal || sourceField.stringValue.isEmpty
        targetField.stringValue = "翻译中…"
        targetField.textColor = darkTheme
            ? NSColor(calibratedWhite: 0.75, alpha: 1)
            : NSColor(calibratedWhite: 0.35, alpha: 1)
        footerField.stringValue = ""
        present()
    }

    func showResult(_ r: TranslationResult) {
        if r.success {
            translated = r.target
            headerLabel.stringValue = Lang.displayPair(r.from, r.to)
            sourceField.stringValue = TextShape.clean(r.source, maxLen: 400)
            sourceField.isHidden = !showOriginal || sourceField.stringValue.isEmpty
            targetField.stringValue = r.target
            targetField.textColor = darkTheme ? .white : NSColor(calibratedWhite: 0.12, alpha: 1)
            footerField.stringValue = "\(r.engine) · 右键复制译文 · 按住可拖动"
        } else {
            translated = ""
            sourceField.isHidden = true
            targetField.stringValue = "翻译失败"
            targetField.textColor = NSColor(calibratedRed: 1, green: 0.48, blue: 0.45, alpha: 1)
            footerField.stringValue = r.error
        }
        present()
    }

    func showMessage(_ message: String, detail: String) {
        headerLabel.stringValue = ""
        sourceField.stringValue = ""
        sourceField.isHidden = true
        translated = ""
        targetField.stringValue = message
        targetField.textColor = darkTheme ? .white : NSColor(calibratedWhite: 0.12, alpha: 1)
        footerField.stringValue = detail
        present()
    }

    // MARK: - 布局

    private func textHeight(_ text: String, font: NSFont, width: CGFloat, maxLines: Int) -> CGFloat {
        if text.isEmpty { return 0 }
        let attr = NSAttributedString(string: text, attributes: [.font: font])
        let rect = attr.boundingRect(with: NSSize(width: width, height: 20000),
                                     options: [.usesLineFragmentOrigin, .usesFontLeading])
        var h = ceil(rect.height) + 2
        if maxLines > 0 {
            let lineH = ceil(font.ascender - font.descender + font.leading)
            h = min(h, lineH * CGFloat(maxLines))
        }
        return h
    }

    private func present() {
        let contentW = CardPanel.cardWidth - padX * 2

        let headerH: CGFloat = 15
        let dividerH: CGFloat = 1
        let footerH: CGFloat = 13

        let sourceH = sourceField.isHidden ? 0 : textHeight(sourceField.stringValue,
                                                            font: sourceField.font ?? NSFont.systemFont(ofSize: 12),
                                                            width: contentW, maxLines: 3)
        let targetH = textHeight(targetField.stringValue,
                                 font: targetField.font ?? NSFont.systemFont(ofSize: fontSize),
                                 width: contentW, maxLines: 0)

        var total = padTop + headerH + gap + dividerH + gap
        if sourceH > 0 { total += sourceH + 7 }
        total += max(14, targetH) + 7 + footerH + padBottom

        let width = CardPanel.cardWidth
        setContentSize(NSSize(width: width, height: max(58, total)))
        guard let container = contentView else { return }
        container.frame = NSRect(x: 0, y: 0, width: width, height: max(58, total))
        backdrop.frame = container.bounds

        // 在 Cocoa 坐标里自上而下摆放
        var top = container.bounds.height - padTop

        headerLabel.frame = NSRect(x: padX, y: top - headerH, width: contentW - 20, height: headerH)
        closeButton.frame = NSRect(x: width - padX - 16, y: top - headerH, width: 16, height: 15)
        top -= headerH + gap

        divider.frame = NSRect(x: padX, y: top - dividerH, width: contentW, height: dividerH)
        top -= dividerH + gap

        if sourceH > 0 {
            sourceField.frame = NSRect(x: padX, y: top - sourceH, width: contentW, height: sourceH)
            top -= sourceH + 7
        }

        let targetDrawH = max(14, targetH)
        targetField.frame = NSRect(x: padX, y: top - targetDrawH, width: contentW, height: targetDrawH)
        top -= targetDrawH + 7

        footerField.frame = NSRect(x: padX, y: top - footerH, width: contentW, height: footerH)

        reposition()
        orderFrontRegardless()
        NSAnimationContext.runAnimationGroup({ ctx in
            ctx.duration = 0.12
            self.animator().alphaValue = 1
        }, completionHandler: nil)
    }

    // MARK: - 定位

    func reposition() {
        guard let first = NSScreen.screens.first else { return }
        let size = frame.size
        let mainHeight = first.frame.height

        let cocoaY = mainHeight - anchorQuartz.y
        var x = anchorQuartz.x + 18
        var y = cocoaY - 24 - size.height

        let probe = NSPoint(x: anchorQuartz.x, y: cocoaY)
        if let screen = screenContaining(probe) {
            let visible = screen.visibleFrame
            if x + size.width > visible.maxX - 8 { x = anchorQuartz.x - size.width - 18 }
            if x < visible.minX + 8 { x = visible.minX + 8 }
            if y < visible.minY + 8 { y = cocoaY + 24 }
            if y + size.height > visible.maxY - 8 { y = visible.maxY - size.height - 8 }
        }

        setFrameOrigin(NSPoint(x: x, y: y))
    }

    private func screenContaining(_ p: NSPoint) -> NSScreen? {
        for s in NSScreen.screens where s.frame.contains(p) {
            return s
        }
        return NSScreen.main
    }

    func hideCard(animated: Bool = true) {
        guard isVisible else { return }
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
        footerField.stringValue = "已复制译文"
    }
}
