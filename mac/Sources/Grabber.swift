import Foundation
import ApplicationServices
import AppKit

struct GrabResult {
    var found = false
    var text = ""
    var route = ""
}

enum Grabber {

    /// 需要「辅助功能」权限，未授权时 AX 调用会直接失败。
    static func isTrusted() -> Bool {
        return AXIsProcessTrusted()
    }

    static func requestTrust() {
        let key = "AXTrustedCheckOptionPrompt" as CFString
        let options = [key: true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
    }

    /// - Parameters:
    ///   - x/y: Quartz 全局坐标（左上角原点，y 向下）
    static func grab(x: CGFloat, y: CGFloat, maxLen: Int) -> GrabResult {
        let systemWide = AXUIElementCreateSystemWide()

        var hit: AXUIElement?
        let err = AXUIElementCopyElementAtPosition(systemWide, Float(x), Float(y), &hit)
        guard err == .success, let start = hit else {
            return GrabResult(found: false, text: "", route: "ax-error-\(err.rawValue)")
        }

        let windowTitle = windowTitleOf(start)

        var current: AXUIElement? = start
        for depth in 0..<5 {
            guard let el = current else { break }

            if let raw = textOf(el) {
                let cleaned = TextShape.clean(raw, maxLen: maxLen)
                if TextShape.worth(cleaned) && cleaned != windowTitle {
                    return GrabResult(found: true, text: cleaned, route: "ax-d\(depth)")
                }
            }
            current = parentOf(el)
        }

        // 子元素兜底：光标停在容器上时，找包含光标的文本子元素
        if let child = deepestTextChild(start, x: x, y: y, depth: 0) {
            let cleaned = TextShape.clean(child, maxLen: maxLen)
            if TextShape.worth(cleaned) && cleaned != windowTitle {
                return GrabResult(found: true, text: cleaned, route: "ax-child")
            }
        }

        return GrabResult(found: false, text: "", route: "ax-empty")
    }

    // MARK: - 属性读取

    private static func attribute(_ el: AXUIElement, _ name: String) -> CFTypeRef? {
        var value: CFTypeRef?
        let err = AXUIElementCopyAttributeValue(el, name as CFString, &value)
        if err == .success { return value }
        return nil
    }

    private static func stringAttribute(_ el: AXUIElement, _ name: String) -> String? {
        guard let v = attribute(el, name) else { return nil }
        if let s = v as? String { return s }
        return nil
    }

    private static func parentOf(_ el: AXUIElement) -> AXUIElement? {
        guard let raw = attribute(el, kAXParentAttribute) else { return nil }
        if CFGetTypeID(raw) == AXUIElementGetTypeID() {
            return unsafeBitCast(raw, to: AXUIElement.self)
        }
        return nil
    }

    private static func roleOf(_ el: AXUIElement) -> String {
        return stringAttribute(el, kAXRoleAttribute) ?? ""
    }

    private static func windowTitleOf(_ el: AXUIElement) -> String {
        var current: AXUIElement? = el
        for _ in 0..<12 {
            guard let e = current else { break }
            if roleOf(e) == "AXWindow" {
                if let t = stringAttribute(e, kAXTitleAttribute), !t.isEmpty {
                    return t.trimmingCharacters(in: .whitespacesAndNewlines)
                }
                return ""
            }
            current = parentOf(e)
        }
        return ""
    }

    private static let skipRoles: Set<String> = [
        "AXButton", "AXMenuItem", "AXMenuBarItem", "AXMenu", "AXMenuBar",
        "AXTab", "AXCheckBox", "AXRadioButton", "AXToolbar", "AXScrollBar",
        "AXSplitter", "AXSlider", "AXProgressIndicator", "AXPopUpButton"
    ]

    private static func textOf(_ el: AXUIElement) -> String? {
        let role = roleOf(el)
        if skipRoles.contains(role) { return nil }

        // 选中文本优先（用户在文本框里选中片段时最准）
        if let sel = stringAttribute(el, kAXSelectedTextAttribute), !sel.isEmpty {
            return sel
        }

        if let value = stringAttribute(el, kAXValueAttribute), !value.isEmpty {
            return value
        }

        if let title = stringAttribute(el, kAXTitleAttribute), !title.isEmpty {
            // 窗口标题、软件名不是待翻译内容
            if role == "AXWindow" || role == "AXApplication" { return nil }
            return title
        }

        if let desc = stringAttribute(el, kAXDescriptionAttribute), !desc.isEmpty {
            if role == "AXWindow" || role == "AXApplication" { return nil }
            return desc
        }

        return nil
    }

    /// 向下最多三层找包含光标的文本元素
    private static func deepestTextChild(_ el: AXUIElement, x: CGFloat, y: CGFloat, depth: Int) -> String? {
        if depth > 2 { return nil }
        guard let raw = attribute(el, kAXChildrenAttribute) else { return nil }
        guard let children = raw as? [AXUIElement] else { return nil }

        for child in children {
            guard contains(child, x: x, y: y) else { continue }
            if let t = textOf(child), TextShape.worth(t) {
                return t
            }
            if let deeper = deepestTextChild(child, x: x, y: y, depth: depth + 1) {
                return deeper
            }
        }
        return nil
    }

    private static func contains(_ el: AXUIElement, x: CGFloat, y: CGFloat) -> Bool {
        var posRef: CFTypeRef?
        var sizeRef: CFTypeRef?
        guard AXUIElementCopyAttributeValue(el, kAXPositionAttribute as CFString, &posRef) == .success,
              AXUIElementCopyAttributeValue(el, kAXSizeAttribute as CFString, &sizeRef) == .success,
              let posRefUnwrapped = posRef, let sizeRefUnwrapped = sizeRef else {
            return false
        }

        var point = CGPoint.zero
        var size = CGSize.zero

        if CFGetTypeID(posRefUnwrapped) == AXValueGetTypeID() {
            let axValue = unsafeBitCast(posRefUnwrapped, to: AXValue.self)
            guard AXValueGetValue(axValue, .cgPoint, &point) else { return false }
        } else {
            return false
        }

        if CFGetTypeID(sizeRefUnwrapped) == AXValueGetTypeID() {
            let axValue = unsafeBitCast(sizeRefUnwrapped, to: AXValue.self)
            guard AXValueGetValue(axValue, .cgSize, &size) else { return false }
        } else {
            return false
        }

        if size.width <= 0 || size.height <= 0 { return false }
        return x >= point.x && x <= point.x + size.width && y >= point.y && y <= point.y + size.height
    }
}
