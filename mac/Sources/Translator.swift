import Foundation

enum Direction: Int {
    case auto = 0
    case enToZh = 1
    case zhToEn = 2
}

struct TranslationResult {
    var success = false
    var source = ""
    var target = ""
    var from = ""
    var to = ""
    var engine = ""
    var error = ""
}

enum Lang {
    static func hasCJK(_ s: String) -> Bool {
        for scalar in s.unicodeScalars {
            let v = scalar.value
            if (0x4E00...0x9FFF).contains(v) || (0x3400...0x4DBF).contains(v)
                || (0x3040...0x30FF).contains(v) || (0xAC00...0xD7AF).contains(v) {
                return true
            }
        }
        return false
    }

    static func resolve(_ dir: Direction, text: String) -> (from: String, to: String) {
        switch dir {
        case .enToZh: return ("en", "zh")
        case .zhToEn: return ("zh", "en")
        case .auto: return hasCJK(text) ? ("zh", "en") : ("en", "zh")
        }
    }

    static func displayPair(_ from: String, _ to: String) -> String {
        let a = from == "zh" ? "中文" : (from == "en" ? "英文" : from)
        let b = to == "zh" ? "中文" : (to == "en" ? "英文" : to)
        return "\(a) → \(b)"
    }
}

enum TextShape {
    static func clean(_ raw: String?, maxLen: Int) -> String {
        guard var s = raw else { return "" }
        s = s.replacingOccurrences(of: "\r", with: " ")
        s = s.replacingOccurrences(of: "\n", with: " ")
        s = s.replacingOccurrences(of: "\t", with: " ")
        s = s.replacingOccurrences(of: "\u{200B}", with: " ")
        s = s.replacingOccurrences(of: "\u{00A0}", with: " ")
        while s.contains("  ") { s = s.replacingOccurrences(of: "  ", with: " ") }
        s = s.trimmingCharacters(in: .whitespacesAndNewlines)
        s = s.trimmingCharacters(in: CharacterSet(charactersIn: "|-·*>•<"))

        if s.count > maxLen {
            let cut = String(s.prefix(maxLen))
            // 尽量在句末标点处断开
            let marks = [".", "!", "?", "。", "！", "？", ";", "；"]
            var best = -1
            for m in marks {
                if let r = cut.range(of: m, options: .backwards) {
                    let idx = cut.distance(from: cut.startIndex, to: r.lowerBound)
                    if idx > maxLen / 3 && idx > best { best = idx }
                }
            }
            if best > 0 {
                s = String(cut.prefix(best + 1)) + "…"
            } else {
                s = cut + "…"
            }
        }
        return s
    }

    static func worth(_ s: String) -> Bool {
        if s.isEmpty { return false }
        for ch in s {
            if ch.isLetter || ch.isNumber { return true }
        }
        return false
    }
}

final class Translator {
    private var cache: [String: TranslationResult] = [:]
    private var order: [String] = []
    private let cacheLimit = 300
    private let lock = NSLock()

    private let userAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 "
        + "(KHTML, like Gecko) Version/17.0 Safari/605.1.15"

    func translate(_ text: String, dir: Direction) -> TranslationResult {
        let (from, to) = Lang.resolve(dir, text: text)
        return translate(text, from: from, to: to)
    }

    func translate(_ text: String, from: String, to: String) -> TranslationResult {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty { return TranslationResult(error: "空文本") }

        let key = "\(from)|\(to)|\(trimmed)"
        lock.lock()
        if let hit = cache[key] {
            lock.unlock()
            return hit
        }
        lock.unlock()

        var last = TranslationResult(error: "无可用翻译引擎")
        if let r = tencent(trimmed, from: from, to: to), r.success {
            remember(key, r)
            return r
        } else if let r = tencent(trimmed, from: from, to: to) {
            last = r
        }
        if let r = youdao(trimmed, from: from, to: to), r.success {
            remember(key, r)
            return r
        } else if let r = youdao(trimmed, from: from, to: to) {
            last = r
        }
        if let r = myMemory(trimmed, from: from, to: to), r.success {
            remember(key, r)
            return r
        } else if let r = myMemory(trimmed, from: from, to: to) {
            last = r
        }
        return last
    }

    private func remember(_ key: String, _ r: TranslationResult) {
        lock.lock()
        defer { lock.unlock() }
        if cache.count >= cacheLimit {
            let drop = max(1, cacheLimit / 4)
            for k in order.prefix(drop) { cache.removeValue(forKey: k) }
            order.removeFirst(min(drop, order.count))
        }
        cache[key] = r
        order.append(key)
    }

    // MARK: - 引擎

    private func tencent(_ text: String, from: String, to: String) -> TranslationResult? {
        let header: [String: Any] = [
            "fn": "auto_translation",
            "client_key": "browser-chrome-131.0.0.0",
            "user_key": "",
            "session_uuid": "",
            "request_type": "translate",
            "timestamp": Int(Date().timeIntervalSince1970 * 1000)
        ]
        let body: [String: Any] = [
            "header": header,
            "type": "plain",
            "model_category": "normal",
            "source": ["lang": from, "text_list": [text]],
            "target": ["lang": to]
        ]

        guard let data = postJSON("https://transmart.qq.com/api/imt", body: body) else { return nil }
        guard let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }

        var r = TranslationResult()
        r.engine = "腾讯交互翻译"
        r.source = text
        r.from = from
        r.to = to

        if let list = obj["auto_translation"] as? [Any], let first = list.first as? String, !first.isEmpty {
            r.target = first.trimmingCharacters(in: .whitespacesAndNewlines)
            r.success = !r.target.isEmpty
            return r
        }
        if let h = obj["header"] as? [String: Any], let code = h["ret_code"] as? String {
            r.error = "ret_code=\(code)"
        } else {
            r.error = "未解析到译文"
        }
        return r
    }

    private func youdao(_ text: String, from: String, to: String) -> TranslationResult? {
        let lc = from == "zh" ? "zh-CHS" : "en"
        let form = "q=\(esc(text))&from=Auto&to=Auto&salt=&sign=&lss=\(lc)"

        guard let data = postForm("https://aidemo.youdao.com/trans", form: form) else { return nil }
        guard let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }

        var r = TranslationResult()
        r.engine = "有道翻译"
        r.source = text
        r.from = from
        r.to = to

        if let list = obj["translation"] as? [Any], let first = list.first as? String, !first.isEmpty {
            r.target = first.trimmingCharacters(in: .whitespacesAndNewlines)
            r.success = !r.target.isEmpty
            return r
        }
        if let code = obj["errorCode"] as? String, code != "0" {
            r.error = "errorCode=\(code)"
        } else {
            r.error = "未解析到译文"
        }
        return r
    }

    private func myMemory(_ text: String, from: String, to: String) -> TranslationResult? {
        let pair = (from == "zh" ? "zh-CN" : "en") + "|" + (to == "zh" ? "zh-CN" : "en")
        let q = esc(text)
        let p = esc(pair)
        let url = "https://api.mymemory.translated.net/get?q=\(q)&langpair=\(p)"

        guard let data = get(url) else { return nil }
        guard let obj = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }

        var r = TranslationResult()
        r.engine = "MyMemory"
        r.source = text
        r.from = from
        r.to = to

        if let d = obj["responseData"] as? [String: Any], let t = d["translatedText"] as? String {
            let s = t.trimmingCharacters(in: .whitespacesAndNewlines)
            if !s.isEmpty && !s.uppercased().contains("QUERY LENGTH LIMIT")
                && !s.uppercased().contains("MYMEMORY WARNING") {
                r.target = s
                r.success = true
                return r
            }
        }
        r.error = "未解析到译文"
        return r
    }

    // MARK: - 按目标语言翻译

    /// 只翻非目标语言的部分，目标语言原样保留。整句已是目标语言时 shouldShow=false。
    func translateForTarget(_ text: String, targetLang: String) -> MixedOutcome {
        var outcome = MixedOutcome()
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        outcome.source = trimmed
        if trimmed.isEmpty {
            outcome.error = "空文本"
            return outcome
        }

        // 命中技术术语就先记下来，后面无论译文有没有变化都要把它讲清楚
        outcome.terms = Terminology.match(trimmed, max: 2)

        if targetLang == "both" {
            let r = translate(trimmed, dir: .auto)
            outcome.shouldShow = r.success || !outcome.terms.isEmpty
            outcome.success = r.success
            outcome.target = r.target
            outcome.from = r.from
            outcome.to = r.to
            outcome.engine = r.engine
            outcome.error = r.error
            outcome.translatedParts = r.success ? 1 : 0
            return outcome
        }

        let targetIsCjk = targetLang == "zh"
        let from = targetIsCjk ? "en" : "zh"
        let to = targetIsCjk ? "zh" : "en"

        var pieces: [String] = []
        var translated = 0
        var engine = ""
        var lastError = ""
        var usedFrom = from
        var usedTo = to

        for seg in LangSplitter.split(trimmed) {
            let isTargetLang = seg.isCjk == targetIsCjk
            if isTargetLang || !seg.isTranslatable {
                pieces.append(seg.text)
                continue
            }

            let core = seg.text.trimmingCharacters(in: .whitespacesAndNewlines)
            // 段尾的空白要保留，中文译文才不会被挤成一片
            let trailing = core.isEmpty ? seg.text : String(seg.text.dropFirst(core.count))

            let r = translate(core, from: from, to: to)
            if r.success && !r.target.isEmpty {
                pieces.append(r.target + trailing)
                translated += 1
                engine = r.engine
                usedFrom = r.from
                usedTo = r.to
            } else {
                pieces.append(seg.text)
                if !r.error.isEmpty { lastError = r.error }
            }
        }

        outcome.translatedParts = translated
        outcome.success = translated > 0
        outcome.shouldShow = translated > 0
        outcome.target = pieces.joined().trimmingCharacters(in: .whitespacesAndNewlines)
        outcome.from = usedFrom
        outcome.to = usedTo
        outcome.engine = engine
        if translated == 0 {
            outcome.error = lastError.isEmpty ? "没有需要翻译的内容" : lastError
        }

        // 译文和原文一样就别弹卡片；但命中了术语要留下，解释本身有价值
        if outcome.shouldShow {
            let a = squash(outcome.target)
            let b = squash(trimmed)
            if a == b && outcome.terms.isEmpty { outcome.shouldShow = false }
        }
        return outcome
    }

    private func squash(_ s: String) -> String {
        return String(s.filter { !$0.isWhitespace })
    }

    // MARK: - 编码

    private func esc(_ s: String) -> String {
        var allowed = CharacterSet.urlQueryAllowed
        allowed.remove(charactersIn: "&+=?#%")
        return s.addingPercentEncoding(withAllowedCharacters: allowed) ?? s
    }

    // MARK: - HTTP

    private func postJSON(_ url: String, body: [String: Any]) -> Data? {
        guard let u = URL(string: url) else { return nil }
        var req = URLRequest(url: u)
        req.httpMethod = "POST"
        req.setValue("application/json; charset=UTF-8", forHTTPHeaderField: "Content-Type")
        req.httpBody = try? JSONSerialization.data(withJSONObject: body, options: [])
        return send(req)
    }

    private func postForm(_ url: String, form: String) -> Data? {
        guard let u = URL(string: url) else { return nil }
        var req = URLRequest(url: u)
        req.httpMethod = "POST"
        req.setValue("application/x-www-form-urlencoded; charset=UTF-8", forHTTPHeaderField: "Content-Type")
        req.httpBody = form.data(using: .utf8)
        return send(req)
    }

    private func get(_ url: String) -> Data? {
        guard let u = URL(string: url) else { return nil }
        return send(URLRequest(url: u))
    }

    private func send(_ request: URLRequest) -> Data? {
        var req = request
        req.timeoutInterval = 8
        req.setValue(userAgent, forHTTPHeaderField: "User-Agent")
        req.setValue("zh-CN,zh;q=0.9,en;q=0.8", forHTTPHeaderField: "Accept-Language")

        let sem = DispatchSemaphore(value: 0)
        var payload: Data?
        let task = URLSession.shared.dataTask(with: req) { data, _, _ in
            payload = data
            sem.signal()
        }
        task.resume()
        _ = sem.wait(timeout: .now() + 10)
        return payload
    }
}

// MARK: - 语言分段

struct LangSegment {
    var text: String
    var isCjk: Bool
    var isTranslatable: Bool
}

struct MixedOutcome {
    var shouldShow = false
    var success = false
    var source = ""
    var target = ""
    var from = ""
    var to = ""
    var engine = ""
    var error = ""
    var translatedParts = 0
    var terms: [Terminology.Term] = []
}

enum LangSplitter {

    static func isCjk(_ c: Character) -> Bool {
        for scalar in c.unicodeScalars {
            let v = scalar.value
            if (0x3400...0x4DBF).contains(v) || (0x4E00...0x9FFF).contains(v)
                || (0xF900...0xFAFF).contains(v) || (0x3040...0x30FF).contains(v)
                || (0xAC00...0xD7AF).contains(v) {
                return true
            }
        }
        return false
    }

    /// 0 = 中性（数字标点空白）, 1 = 中日韩, 2 = 其它语言字母
    private static func classOf(_ c: Character) -> Int {
        guard c.isLetter else { return 0 }
        return isCjk(c) ? 1 : 2
    }

    private static func translatable(_ text: String, isCjk: Bool) -> Bool {
        if isCjk {
            return text.contains { LangSplitter.isCjk($0) }
        }
        let letters = text.reduce(0) { $0 + ($1.isLetter ? 1 : 0) }
        return letters >= 2
    }

    static func split(_ text: String) -> [LangSegment] {
        var result: [LangSegment] = []
        var buffer = ""
        var current = 0

        func flush() {
            if buffer.isEmpty { return }
            result.append(LangSegment(text: buffer, isCjk: current == 1,
                                      isTranslatable: translatable(buffer, isCjk: current == 1)))
            buffer = ""
        }

        for ch in text {
            let cls = classOf(ch)
            if cls == 0 {
                buffer.append(ch)
                continue
            }
            if current == 0 {
                current = cls
                buffer.append(ch)
                continue
            }
            if cls == current {
                buffer.append(ch)
                continue
            }
            flush()
            current = cls
            buffer.append(ch)
        }
        flush()

        var merged: [LangSegment] = []
        for s in result {
            if !merged.isEmpty && s.text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                merged[merged.count - 1].text += s.text
            } else {
                merged.append(s)
            }
        }
        return merged
    }

    /// 本地判断：这段文本里有多少片段需要翻译（不发网络请求）
    static func countTranslatable(_ text: String, targetIsCjk: Bool) -> Int {
        return split(text).filter { $0.isCjk != targetIsCjk && $0.isTranslatable }.count
    }
}