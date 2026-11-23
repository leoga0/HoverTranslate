import Foundation

/// 用户自建的行业/岗位。
struct UserIndustry: Codable {
    var key: String
    var label: String
}

/// 用户自建的词条。
struct UserTerm: Codable {
    var i: String   // 所属行业 key
    var n: String   // 名称
    var a: String   // 别名，| 分隔
    var d: String   // 一句话解释
    var x: String   // 生活里的比方
}

/// 自定义词库：行业（含岗位）和词条都存在 Application Support 下的 json 里，
/// 菜单里能直接加，也可以手工编辑文件。
final class UserDict {

    static let shared = UserDict()

    private(set) var industries: [UserIndustry] = []
    private(set) var terms: [UserTerm] = []

    private static var dirURL: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        let dir = base.appendingPathComponent("HoverTranslate", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    static var industriesURL: URL { return dirURL.appendingPathComponent("user-industries.json") }
    static var termsURL: URL { return dirURL.appendingPathComponent("user-terms.json") }

    private init() {
        load()
    }

    func load() {
        industries = decode([UserIndustry].self, at: UserDict.industriesURL) ?? []
        terms = decode([UserTerm].self, at: UserDict.termsURL) ?? []

        // 过滤掉非法条目，免得手改文件写坏了整个词库
        industries = industries.filter { !$0.key.isEmpty && !$0.label.isEmpty }
        terms = terms.filter { !$0.n.isEmpty && !$0.d.isEmpty }
    }

    private func decode<T: Decodable>(_ type: T.Type, at url: URL) -> T? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        return try? JSONDecoder().decode(type, from: data)
    }

    /// 新增行业/岗位，返回它的 key；重名直接返回已有 key。
    @discardableResult
    func addIndustry(_ label: String) -> String? {
        let name = label.trimmingCharacters(in: .whitespacesAndNewlines)
        if name.isEmpty { return nil }

        for (k, v) in IndustryTerms.builtinDomains where v == name { return k }
        for ind in industries where ind.label == name { return ind.key }

        let key = "u\(industries.count + 1)-\(String(UUID().uuidString.prefix(4)).lowercased())"
        industries.append(UserIndustry(key: key, label: name))
        saveIndustries()
        return key
    }

    func addTerm(_ term: UserTerm) {
        if term.n.isEmpty || term.d.isEmpty { return }
        terms.append(term)
        saveTerms()
    }

    func label(for key: String) -> String {
        for ind in industries where ind.key == key { return ind.label }
        return "自定义"
    }

    private func saveIndustries() {
        if let data = try? JSONEncoder().encode(industries) {
            try? data.write(to: UserDict.industriesURL)
        }
    }

    private func saveTerms() {
        if let data = try? JSONEncoder().encode(terms) {
            try? data.write(to: UserDict.termsURL)
        }
    }

    /// 转成匹配引擎用的条目。
    func toTerms() -> [Terminology.Term] {
        return terms.map { t in
            Terminology.Term(n: t.n, a: t.a, d: t.d, x: t.x,
                             c: label(for: t.i), i: t.i)
        }
    }
}
