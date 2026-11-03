import Foundation

struct RadioStation: Identifiable, Codable, Hashable {
    let stationuuid: String
    let name: String
    let country: String
    let url: String?
    let urlResolved: String
    let favicon: String?
    let homepage: String?
    let votes: Int?
    let clickcount: Int?
    let language: String?
    let tags: String?

    var id: String { stationuuid }

    var genres: [String] {
        guard let tags, !tags.isEmpty else { return [] }
        return tags
            .split(separator: ",")
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }
    }

    enum CodingKeys: String, CodingKey {
        case stationuuid
        case name
        case country
        case url
        case urlResolved = "url_resolved"
        case favicon
        case homepage
        case votes
        case clickcount
        case language
        case tags
    }

    static func == (lhs: RadioStation, rhs: RadioStation) -> Bool {
        lhs.stationuuid == rhs.stationuuid
    }

    func hash(into hasher: inout Hasher) {
        hasher.combine(stationuuid)
    }
}
