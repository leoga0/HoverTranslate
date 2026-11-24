import Foundation

/// 开机自启（macOS）：写一个用户级 LaunchAgent，登录时拉起，不需要管理员权限。
/// 以前没做过自启，Windows 那边有菜单开关，mac 这边一直缺，这次补齐。
enum MacAutoStart {
    static let label = "com.leoga0.hovertranslate"

    static var plistPath: String {
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        return home + "/Library/LaunchAgents/" + label + ".plist"
    }

    static var isEnabled: Bool {
        return FileManager.default.fileExists(atPath: plistPath)
    }

    /// 当前 app 包里的可执行文件。Bundle.main.executablePath 在 app bundle 里指向 Contents/MacOS/。
    static var executablePath: String? {
        if let exe = Bundle.main.executablePath, FileManager.default.fileExists(atPath: exe) {
            return exe
        }
        return nil
    }

    @discardableResult
    static func enable() -> Bool {
        guard let exe = executablePath else {
            Config.log("autostart: 找不到可执行文件")
            return false
        }

        let dir = (plistPath as NSString).deletingLastPathComponent
        try? FileManager.default.createDirectory(atPath: dir, withIntermediateDirectories: true, attributes: nil)

        let body = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>Label</key>
          <string>\(label)</string>
          <key>ProgramArguments</key>
          <array>
            <string>\(exe)</string>
            <string>--autostart</string>
          </array>
          <key>RunAtLoad</key>
          <true/>
          <key>KeepAlive</key>
          <false/>
          <key>ProcessType</key>
          <string>Interactive</string>
          <key>LimitLoadToSessionType</key>
          <string>Aqua</string>
        </dict>
        </plist>

        """

        do {
            try body.write(toFile: plistPath, atomically: true, encoding: .utf8)
        } catch {
            Config.log("autostart: 写 plist 失败 \(error)")
            return false
        }

        launchctl(["unload", plistPath])
        launchctl(["load", "-w", plistPath])
        Config.log("autostart: 开 \(plistPath)")
        return true
    }

    static func disable() {
        launchctl(["unload", plistPath])
        try? FileManager.default.removeItem(atPath: plistPath)
        Config.log("autostart: 关")
    }

    /// 配置说该开、实际没装上（挪过 app、plist 被清掉），启动时补回来。
    static func ensure(_ wanted: Bool) {
        if wanted && !isEnabled {
            _ = enable()
        }
    }

    private static func launchctl(_ args: [String]) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/launchctl")
        p.arguments = args
        do {
            try p.run()
            p.waitUntilExit()
        } catch {
            Config.log("launchctl: \(error)")
        }
    }
}
