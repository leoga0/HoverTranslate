import Foundation

/// 联网解释：把选中的文本交给模型，用「一句话 + 生活比方」讲清楚里面的技术名词。
/// 凭证复用本机 DSH 的 DEEPSEEK_API_KEY。
final class AiExplainer {

    static let shared = AiExplainer()

    private var keyLoaded = false
    private var key: String?
    private var cache: [String: [Terminology.Term]] = [:]
    private var order: [String] = []
    private let cacheLimit = 120
    private let lock = NSLock()

    var available: Bool {
        return apiKey != nil
    }

    private var apiKey: String? {
        if keyLoaded { return key }
        keyLoaded = true
        let path = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".dsh/.credentials.yaml")
        guard let content = try? String(contentsOf: path, encoding: .utf8) else { return nil }

        for line in content.components(separatedBy: "\n") {
            guard let r = line.range(of: "DEEPSEEK_API_KEY:") else { continue }
            var v = String(line[r.upperBound...]).trimmingCharacters(in: .whitespaces)
            v = v.trimmingCharacters(in: CharacterSet(charactersIn: "\"'"))
            if v.count > 10 {
                key = v
                return key
            }
        }
        return nil
    }

    /// 挑出文本里最难的 1-3 个词，给成人话解释。失败返回空数组。
    func explain(_ text: String, max: Int) -> [Terminology.Term] {
        if text.isEmpty || max <= 0 { return [] }

        var key = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if key.count > 160 { key = String(key.prefix(160)) }

        lock.lock()
        if let hit = cache[key] {
            lock.unlock()
            return hit
        }
        lock.unlock()

        guard let token = apiKey else { return [] }

        let system = """
        你在给完全不懂技术的人解释他看到的文字里的技术名词。每条格式严格为「词｜解释」，一行一条，\
        不要编号、不要前缀、不要开场白。解释要求：先用一句话说清它是什么（不许用别的术语来解释），\
        再用一个日常生活里的例子打比方，整条不超过 70 个汉字。挑其中最可能让人看不懂的，最多 \(max) 条。\
        如果这段文字里没有任何技术名词，只输出一个字：无
        """

        let body: [String: Any] = [
            "model": "deepseek-chat",
            "temperature": 0.3,
            "max_tokens": 400,
            "stream": false,
            "messages": [
                ["role": "system", "content": system],
                ["role": "user", "content": "文字：" + key]
            ]
        ]

        guard let data = try? JSONSerialization.data(withJSONObject: body, options: []),
              let url = URL(string: "https://api.deepseek.com/chat/completions") else {
            return []
        }

        var req = URLRequest(url: url)
        req.httpMethod = "POST"
        req.timeoutInterval = 15
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        req.setValue("Bearer " + token, forHTTPHeaderField: "Authorization")
        req.httpBody = data

        let sem = DispatchSemaphore(value: 0)
        var payload: Data?
        URLSession.shared.dataTask(with: req) { d, _, _ in
            payload = d
            sem.signal()
        }.resume()
        _ = sem.wait(timeout: .now() + 18)

        var result: [Terminology.Term] = []
        if let payload = payload,
           let obj = try? JSONSerialization.jsonObject(with: payload) as? [String: Any],
           let choices = obj["choices"] as? [[String: Any]],
           let message = choices.first?["message"] as? [String: Any],
           let content = message["content"] as? String {

            for rawLine in content.components(separatedBy: "\n") {
                let line = rawLine.trimmingCharacters(in: .whitespaces)
                if line.count < 4 { continue }
                if line == "无" || line.hasPrefix("无｜") { continue }

                var sep = line.range(of: "｜")
                if sep == nil { sep = line.range(of: "|") }
                guard let s = sep else { continue }

                let term = String(line[..<s.lowerBound])
                    .trimmingCharacters(in: CharacterSet(charactersIn: " *`「」『』"))
                let explain = String(line[s.upperBound...])
                    .trimmingCharacters(in: CharacterSet(charactersIn: " *`"))

                if term.isEmpty || term.count > 24 || explain.count < 6 { continue }

                result.append(Terminology.Term(n: term, a: "", d: explain, x: "", c: "AI 解释"))
                if result.count >= max { break }
            }
        }

        lock.lock()
        if cache.count >= cacheLimit {
            let drop = Swift.max(1, cacheLimit / 4)
            for k in order.prefix(drop) { cache.removeValue(forKey: k) }
            order.removeFirst(Swift.min(drop, order.count))
        }
        cache[key] = result
        order.append(key)
        lock.unlock()

        return result
    }
}