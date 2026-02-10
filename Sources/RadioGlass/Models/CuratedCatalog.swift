import Foundation

struct CountryPreset: Identifiable, Hashable {
    let id: String
    let displayName: String
    let apiName: String
    let topBrands: [String]
    
    var countryCode: String {
        switch id {
        case "uk": return "GB"
        default: return id.uppercased()
        }
    }

    static let all: [CountryPreset] = [
        .init(id: "uk", displayName: "United Kingdom", apiName: "United Kingdom", topBrands: ["BBC Radio 1", "BBC Radio 2", "BBC Radio 4", "BBC 6 Music", "Capital", "Virgin Radio UK", "Heart", "Classic FM", "LBC", "talkSPORT"]),
        .init(id: "us", displayName: "United States", apiName: "United States", topBrands: ["NPR", "KIIS", "Z100", "KEXP", "Hot 97", "iHeart", "1010 WINS", "WNYC", "KROQ", "SiriusXM"]),
        .init(id: "ca", displayName: "Canada", apiName: "Canada", topBrands: ["CBC Radio One", "CBC Music", "Virgin Radio", "CHUM", "CFOX", "CHFI", "Boom", "98.1 CHFI"]),
        .init(id: "au", displayName: "Australia", apiName: "Australia", topBrands: ["triple j", "ABC Radio", "Nova", "KIIS", "2GB", "3AW", "Smooth FM", "Gold 104.3"]),
        .init(id: "nz", displayName: "New Zealand", apiName: "New Zealand", topBrands: ["ZM", "The Edge", "Newstalk ZB", "The Rock", "More FM", "RNZ National"]),
        .init(id: "ie", displayName: "Ireland", apiName: "Ireland", topBrands: ["RTÉ Radio 1", "RTÉ 2FM", "Today FM", "Newstalk", "Spin", "Classic Hits"]),
        .init(id: "de", displayName: "Germany", apiName: "Germany", topBrands: ["1LIVE", "WDR", "NDR 2", "Antenne Bayern", "Bayern 3", "SWR3", "Radio Hamburg", "Deutschlandfunk"]),
        .init(id: "fr", displayName: "France", apiName: "France", topBrands: ["France Inter", "RTL", "NRJ", "Europe 1", "RMC", "Skyrock", "France Info", "Nostalgie"]),
        .init(id: "es", displayName: "Spain", apiName: "Spain", topBrands: ["Cadena SER", "COPE", "Los 40", "Onda Cero", "RNE", "Kiss FM", "Europa FM"]),
        .init(id: "it", displayName: "Italy", apiName: "Italy", topBrands: ["RTL 102.5", "Radio Deejay", "RDS", "Radio Italia", "Radio 105", "Virgin Radio Italia", "RAI Radio 1"]),
        .init(id: "nl", displayName: "Netherlands", apiName: "Netherlands", topBrands: ["NPO Radio 1", "NPO Radio 2", "Radio 538", "Sky Radio", "Qmusic", "3FM"]),
        .init(id: "be", displayName: "Belgium", apiName: "Belgium", topBrands: ["Radio 2", "Qmusic", "Studio Brussel", "MNM", "Bel RTL", "Nostalgie"]),
        .init(id: "se", displayName: "Sweden", apiName: "Sweden", topBrands: ["Sveriges Radio P1", "P3", "Mix Megapol", "RIX FM", "NRJ Sweden"]),
        .init(id: "no", displayName: "Norway", apiName: "Norway", topBrands: ["NRK P1", "NRK P3", "P4", "Radio Norge", "NRJ Norway"]),
        .init(id: "dk", displayName: "Denmark", apiName: "Denmark", topBrands: ["DR P1", "DR P3", "NOVA", "The Voice", "Radio4"]),
        .init(id: "fi", displayName: "Finland", apiName: "Finland", topBrands: ["Yle Radio Suomi", "YleX", "Radio Nova", "NRJ Finland", "SuomiPop"]),
        .init(id: "pl", displayName: "Poland", apiName: "Poland", topBrands: ["RMF FM", "Radio ZET", "Polskie Radio", "Eska", "TOK FM"]),
        .init(id: "pt", displayName: "Portugal", apiName: "Portugal", topBrands: ["RFM", "Rádio Comercial", "Antena 1", "TSF", "M80"]),
        .init(id: "ch", displayName: "Switzerland", apiName: "Switzerland", topBrands: ["SRF 1", "SRF 3", "Radio 24", "Radio Energy", "Couleur 3"]),
        .init(id: "at", displayName: "Austria", apiName: "Austria", topBrands: ["Hitradio Ö3", "FM4", "Kronehit", "Radio Wien", "Antenne Steiermark"]),
        .init(id: "cz", displayName: "Czechia", apiName: "Czech Republic", topBrands: ["Radiožurnál", "Evropa 2", "Frekvence 1", "ČRo Plus", "Impuls"]),
        .init(id: "jp", displayName: "Japan", apiName: "Japan", topBrands: ["NHK Radio 1", "J-WAVE", "TOKYO FM", "TBS Radio", "Nippon Broadcasting"]),
        .init(id: "kr", displayName: "South Korea", apiName: "Korea, Republic of", topBrands: ["KBS", "SBS Power FM", "MBC FM4U", "Arirang Radio"]),
        .init(id: "in", displayName: "India", apiName: "India", topBrands: ["AIR FM Gold", "Radio Mirchi", "Red FM", "Big FM", "Radio City"]),
        .init(id: "sg", displayName: "Singapore", apiName: "Singapore", topBrands: ["CNA938", "Class 95", "987", "Kiss92", "Gold 905"]),
        .init(id: "my", displayName: "Malaysia", apiName: "Malaysia", topBrands: ["HITZ", "ERA", "Lite", "MIX", "Sinar"]),
        .init(id: "za", displayName: "South Africa", apiName: "South Africa", topBrands: ["Metro FM", "5FM", "947", "Kaya 959", "702"]),
        .init(id: "br", displayName: "Brazil", apiName: "Brazil", topBrands: ["Jovem Pan", "CBN", "BandNews FM", "Antena 1", "Transamérica"]),
        .init(id: "mx", displayName: "Mexico", apiName: "Mexico", topBrands: ["Los 40", "W Radio", "Exa FM", "Radio Fórmula", "Imagen Radio"]),
        .init(id: "ar", displayName: "Argentina", apiName: "Argentina", topBrands: ["Radio Mitre", "La 100", "Cadena 3", "Continental", "Metro"])
    ]
}
