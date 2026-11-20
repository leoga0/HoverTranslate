import Cocoa

final class CardPanel: NSPanel {

    static let cardWidth: CGFloat = 380
    private let textInset: CGFloat = 14

    private let backdrop = NSVisualEffectView()
    private let stack = NSStackView()
    private let pairLabel = NSTextField(labelWithString: "")
    private let sourceLabel = NSTextField(wrappingLabelWithString: "")
    private let targetLabel = NSTextField(wrappingLabelWithString: "")
    private let footerLabel = NSTextField(labelWithString: "")
    private let separator = NSBox()

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

        backdrop.material = .hudWindow
        backdrop.blendingMode = .behindWindow
        backdrop.state = .active
        backdrop.wantsLayer = true
        backdrop.layer?.cornerRadius = 10
        backdrop.layer?.masksToBounds = true
        backdrop.translatesAutoresizingMaskIntoConstraints = false

        let container = NSView()
        container.wantsLayer = true
        container.translatesAutoresizingMaskIntoConstraints = false
        contentView = container
        container.addSubview(backdrop)
        NSLayoutConstraint.activate([
            backdrop.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            backdrop.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            backdrop.topAnchor.constraint(equalTo: container.topAnchor),
            backdrop.bottomAnchor.constraint(equalTo: container.bottomAnchor)
        ])

        pairLabel.font = NSFont.systemFont(ofSize: 11, weight: .medium)
        pairLabel.textColor = NSColor(calibratedRed: 0.62, green: 0.78, blue: 1.0, alpha: 1)

        sourceLabel.font = NSFont.systemFont(ofSize: 12)
        sourceLabel.textColor = NSColor(calibratedWhite: 0.78, alpha: 1)
        sourceLabel.preferredMaxLayoutWidth = CardPanel.cardWidth - textInset * 2
        sourceLabel.maximumNumberOfLines = 3

        targetLabel.font = NSFont.systemFont(ofSize: fontSize)
        targetLabel.textColor = .white
        targetLabel.preferredMaxLayoutWidth = CardPanel.cardWidth - textInset * 2
        targetLabel.maximumNumberOfLines = 0

        footerLabel.font = NSFont.systemFont(ofSize: 10)
        footerLabel.textColor = NSColor(calibratedWhite: 0.62, alpha: 1)

        separator.boxType = .separator
        separator.translatesAutoresizingMaskIntoConstraints = false

        let closeButton = NSButton(title: "×", target: self, action: #selector(closeTapped))
        closeButton.isBordered = false
        closeButton.font = NSFont.systemFont(ofSize: 14)
        closeButton.contentTintColor = NSColor(calibratedWhite: 0.7, alpha: 1)
        closeButton.translatesAutoresizingMaskIntoConstraints = false
        closeButton.widthAnchor.constraint(equalToConstant: 18).isActive = true

        let headerRow = NSStackView(views: [pairLabel, closeButton])
        headerRow.orientation = .horizontal
        headerRow.alignment = .centerY
        headerRow.distribution = .fill
        headerRow.spacing = 8
        pairLabel.setContentHuggingPriority(.defaultLow, for: .horizontal)

        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 6
        stack.edgeInsets = NSEdgeInsets(top: 10, left: textInset, bottom: 11, right: textInset)
        stack.translatesAutoresizingMaskIntoConstraints = false
        stack.addArrangedSubview(headerRow)
        stack.addArrangedSubview(separator)
        stack.addArrangedSubview(sourceLabel)
        stack.addArrangedSubview(targetLabel)
        stack.addArrangedSubview(footerLabel)
        container.addSubview(stack)

        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: container.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: container.trailingAnchor),
            stack.topAnchor.constraint(equalTo: container.topAnchor),
            stack.bottomAnchor.constraint(equalTo: container.bottomAnchor),
            headerRow.widthAnchor.constraint(equalTo: stack.widthAnchor, constant: -textInset * 2),
            separator.widthAnchor.constraint(equalTo: stack.widthAnchor, constant: -textInset * 2)
        ])

        appliesTheme()
    }

    override var canBecomeKey: Bool { return false }
    override var canBecomeMain: Bool { return false }

    // MARK: - 内容

    func appliesTheme() {
        pairLabel.textColor = darkTheme
            ? NSColor(calibratedRed: 0.62, green: 0.78, blue: 1.0, alpha: 1)
            : NSColor(calibratedRed: 0.11, green: 0.39, blue: 0.82, alpha: 1)
        sourceLabel.textColor = darkTheme
            ? NSColor(calibratedWhite: 0.80, alpha: 1)
            : NSColor(calibratedWhite: 0.38, alpha: 1)
        targetLabel.textColor = darkTheme ? .white : NSColor(calibratedWhite: 0.12, alpha: 1)
        footerLabel.textColor = darkTheme
            ? NSColor(calibratedWhite: 0.62, alpha: 1)
            : NSColor(calibratedWhite: 0.48, alpha: 1)
        targetLabel.font = NSFont.systemFont(ofSize: fontSize)
        sourceLabel.font = NSFont.systemFont(ofSize: max(10, fontSize - 3))
        backdrop.material = darkTheme ? .hudWindow : .popover
    }

    func showLoading(source: String, pair: String, quartzX: CGFloat, quartzY: CGFloat) {
        anchorQuartz = NSPoint(x: quartzX, y: quartzY)
        translated = ""
        pairLabel.stringValue = pair
        sourceLabel.stringValue = TextShape.clean(source, maxLen: 400)
        sourceLabel.isHidden = !showOriginal || sourceLabel.stringValue.isEmpty
        targetLabel.stringValue = "翻译中…"
        targetLabel.textColor = NSColor(calibratedWhite: 0.75, alpha: 1)
        footerLabel.stringValue = ""
        present()
    }

    func showResult(_ r: TranslationResult) {
        if r.success {
            translated = r.target
            pairLabel.stringValue = Lang.displayPair(r.from, r.to)
            sourceLabel.stringValue = TextShape.clean(r.source, maxLen: 400)
            sourceLabel.isHidden = !showOriginal || sourceLabel.stringValue.isEmpty
            targetLabel.stringValue = r.target
            targetLabel.textColor = darkTheme ? .white : NSColor(calibratedWhite: 0.12, alpha: 1)
            footerLabel.stringValue = "\(r.engine) · 右键复制译文 · 按住可拖动"
        } else {
            translated = ""
            targetLabel.stringValue = "翻译失败"
            targetLabel.textColor = NSColor(calibratedRed: 1, green: 0.48, blue: 0.45, alpha: 1)
            footerLabel.stringValue = r.error
        }
        present()
    }

    func showMessage(_ message: String, detail: String) {
        pairLabel.stringValue = ""
        sourceLabel.stringValue = ""
        sourceLabel.isHidden = true
        targetLabel.stringValue = message
        targetLabel.textColor = darkTheme ? .white : NSColor(calibratedWhite: 0.12, alpha: 1)
        footerLabel.stringValue = detail
        present()
    }

    // MARK: - 显示与定位

    private func present() {
        layoutIfNeeded()
        contentView?.layoutSubtreeIfNeeded()
        let fitting = stack.fittingSize
        let height = max(60, fitting.height)
        setContentSize(NSSize(width: CardPanel.cardWidth, height: height))
        reposition()
        orderFrontRegardless()
        NSAnimationContext.runAnimationGroup { ctx in
            ctx.duration = 0.12
            animator().alphaValue = 1
        }
    }

    func reposition() {
        let size = frame.size
        let mainHeight = NSScreen.screens.first?.frame.height ?? 1080
        // Quartz（左上原点）转 Cocoa（左下原点）
        let cocoaY = mainHeight - anchorQuartz.y

        var x = anchorQuartz.x + 18
        var y = cocoaY - 24 - size.height

        // 优先放在鼠标右下；越界则翻到左上
        if let screen = screenContaining(NSPoint(x: anchorQuartz.x, y: cocoaY)) {
            let visible = screen.visibleFrame
            if x + size.width > visible.maxX - 8 { x = anchorQuartz.x - size.width - 18 }
            if y < visible.minY + 8 { y = cocoaY + 24 }
            if x < visible.minX + 8 { x = visible.minX + 8 }
            if y + size.height > visible.maxY - 8 { y = visible.maxY - size.height - 8 }
        }

        setFrameOrigin(NSPoint(x: x, y: y))
    }

    private func screenContaining(_ p: NSPoint) -> NSScreen? {
        for s in NSScreen.screens {
            if s.frame.contains(p) { return s }
        }
        return NSScreen.main
    }

    func hideCard(animated: Bool = true) {
        guard isVisible else { return }
        if animated {
            NSAnimationContext.runAnimationGroup({ ctx in
                ctx.duration = 0.14
                animator().alphaValue = 0
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
        let text = translated.isEmpty ? targetLabel.stringValue : translated
        guard !text.isEmpty else { return }
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.setString(text, forType: .string)
        footerLabel.stringValue = "已复制译文"
    }
}
