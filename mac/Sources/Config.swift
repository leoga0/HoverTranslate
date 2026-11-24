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
            return v <= 0 ? 0.22 : v
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

    /// 选中的行业/岗位，逗号分隔，可多选；空表示不偏向。
    var industries: String {
        get {
            if let v = defaults.string(forKey: prefix + "industries") { return v }
            // 老版本只存了单选 industry，读的时候迁过来
            let single = defaults.string(forKey: prefix + "industry") ?? ""
            return single == "auto" ? "" : single
        }
        set { defaults.set(newValue, forKey: prefix + "industries") }
    }

    /// 主选那一个，给「给当前行业加词条」这类只认单个行业的入口用。
    var industry: String {
        get {
            let v = industries
            guard let first = v.split(separator: ",").first else { return "auto" }
            return String(first)
        }
        set {
            industries = (newValue == "auto" || newValue.isEmpty) ? "" : newValue
        }
    }

    var liftLines: Int {
        get {
            let v = defaults.integer(forKey: prefix + "liftLines")
            return min(4, max(0, v))
        }
        set { defaults.set(newValue, forKey: prefix + "liftLines") }
    }

    var cardAnchor: Int {
        get { return defaults.integer(forKey: prefix + "cardAnchor") }
        set { defaults.set(newValue, forKey: prefix + "cardAnchor") }
    }

    /// 开机自启：默认开，菜单里点一下可以关。
    var autoStart: Bool {
        get {
            if defaults.object(forKey: prefix + "autoStart") == nil { return true }
            return defaults.bool(forKey: prefix + "autoStart")
        }
        set { defaults.set(newValue, forKey: prefix + "autoStart") }
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
