import Foundation

final class Config {
    static let shared = Config()

    private let defaults = UserDefaults.standard
    private let prefix = "HoverTranslate."

    var direction: Direction {
        get { return Direction(rawValue: defaults.integer(forKey: prefix + "direction")) ?? .auto }
        set { defaults.set(newValue.rawValue, forKey: prefix + "direction") }
    }

    var hoverEnabled: Bool {
        get {
            if defaults.object(forKey: prefix + "hoverEnabled") == nil { return true }
            return defaults.bool(forKey: prefix + "hoverEnabled")
        }
        set { defaults.set(newValue, forKey: prefix + "hoverEnabled") }
    }

    var hoverDelay: TimeInterval {
        get {
            let v = defaults.double(forKey: prefix + "hoverDelay")
            return v <= 0 ? 0.34 : v
        }
        set { defaults.set(newValue, forKey: prefix + "hoverDelay") }
    }

    var maxTextLength: Int {
        get {
            let v = defaults.integer(forKey: prefix + "maxTextLength")
            return v <= 0 ? 240 : v
        }
        set { defaults.set(newValue, forKey: prefix + "maxTextLength") }
    }

    var showOriginal: Bool {
        get {
            if defaults.object(forKey: prefix + "showOriginal") == nil { return false }
            return defaults.bool(forKey: prefix + "showOriginal")
        }
        set { defaults.set(newValue, forKey: prefix + "showOriginal") }
    }

    var darkTheme: Bool {
        get {
            if defaults.object(forKey: prefix + "darkTheme") == nil { return true }
            return defaults.bool(forKey: prefix + "darkTheme")
        }
        set { defaults.set(newValue, forKey: prefix + "darkTheme") }
    }

    var targetLang: String {
        get { return defaults.string(forKey: prefix + "targetLang") ?? "zh" }
        set { defaults.set(newValue, forKey: prefix + "targetLang") }
    }

    var selectionExplain: Bool {
        get {
            if defaults.object(forKey: prefix + "selectionExplain") == nil { return true }
            return defaults.bool(forKey: prefix + "selectionExplain")
        }
        set { defaults.set(newValue, forKey: prefix + "selectionExplain") }
    }

    var industry: String {
        get {
            let v = defaults.string(forKey: prefix + "industry") ?? "auto"
            return IndustryTerms.isKnown(v) ? v : "auto"
        }
        set { defaults.set(newValue, forKey: prefix + "industry") }
    }

    var cardAnchor: Int {
        get { return defaults.integer(forKey: prefix + "cardAnchor") }
        set { defaults.set(newValue, forKey: prefix + "cardAnchor") }
    }

    var fontSize: CGFloat {
        get {
            let v = defaults.double(forKey: prefix + "fontSize")
            return v <= 0 ? 14 : CGFloat(v)
        }
        set { defaults.set(Double(newValue), forKey: prefix + "fontSize") }
    }

    static var logURL: URL {
        let dir = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Logs/HoverTranslate", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true, attributes: nil)
        return dir.appendingPathComponent("hover.log")
    }

    static func log(_ message: String) {
        let ts = ISO8601DateFormatter().string(from: Date())
        let line = "\(ts)  \(message)\n"
        if let data = line.data(using: .utf8) {
            if let handle = try? FileHandle(forWritingTo: logURL) {
                handle.seekToEndOfFile()
                handle.write(data)
                try? handle.close()
            } else {
                try? data.write(to: logURL)
            }
        }
    }
}
