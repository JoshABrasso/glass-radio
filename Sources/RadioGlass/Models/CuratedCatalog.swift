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

    static let base: [CountryPreset] = [
        .init(id: "ar", displayName: "Argentina", apiName: "Argentina", topBrands: ["Radio Mitre", "La 100", "Cadena 3", "Continental", "Metro"]),
        .init(id: "au", displayName: "Australia", apiName: "Australia", topBrands: ["triple j", "ABC Radio", "Nova", "KIIS", "2GB", "3AW", "Smooth FM", "Gold 104.3"]),
        .init(id: "at", displayName: "Austria", apiName: "Austria", topBrands: ["Hitradio Ö3", "FM4", "Kronehit", "Radio Wien", "Antenne Steiermark"]),
        .init(id: "be", displayName: "Belgium", apiName: "Belgium", topBrands: ["Radio 2", "Qmusic", "Studio Brussel", "MNM", "Bel RTL", "Nostalgie"]),
        .init(id: "br", displayName: "Brazil", apiName: "Brazil", topBrands: ["Jovem Pan", "CBN", "BandNews FM", "Antena 1", "Transamérica"]),
        .init(id: "bg", displayName: "Bulgaria", apiName: "Bulgaria", topBrands: ["Darik Radio", "Radio 1", "NRJ Bulgaria", "BNT Radio"]),
        .init(id: "ca", displayName: "Canada", apiName: "Canada", topBrands: ["CBC Radio One", "CBC Music", "Virgin Radio", "CHUM", "CFOX", "CHFI", "Boom", "98.1 CHFI"]),
        .init(id: "cl", displayName: "Chile", apiName: "Chile", topBrands: ["Radio Cooperativa", "Bio Bio", "ADN Radio", "Radio Carolina", "Rock & Pop"]),
        .init(id: "co", displayName: "Colombia", apiName: "Colombia", topBrands: ["Caracol Radio", "W Radio", "La FM", "Blu Radio", "RCN Radio"]),
        .init(id: "hr", displayName: "Croatia", apiName: "Croatia", topBrands: ["Otvoreni", "Radio Dalmacija", "HR1", "HR2", "Narodni"]),
        .init(id: "cz", displayName: "Czechia", apiName: "Czech Republic", topBrands: ["Radiožurnál", "Evropa 2", "Frekvence 1", "ČRo Plus", "Impuls"]),
        .init(id: "dk", displayName: "Denmark", apiName: "Denmark", topBrands: ["DR P1", "DR P3", "NOVA", "The Voice", "Radio4"]),
        .init(id: "eg", displayName: "Egypt", apiName: "Egypt", topBrands: ["Nogoum FM", "Nile FM", "Radio Masr", "Mega FM", "Hits FM"]),
        .init(id: "fi", displayName: "Finland", apiName: "Finland", topBrands: ["Yle Radio Suomi", "YleX", "Radio Nova", "NRJ Finland", "SuomiPop"]),
        .init(id: "fr", displayName: "France", apiName: "France", topBrands: ["France Inter", "RTL", "NRJ", "Europe 1", "RMC", "Skyrock", "France Info", "Nostalgie"]),
        .init(id: "de", displayName: "Germany", apiName: "Germany", topBrands: ["1LIVE", "WDR", "NDR 2", "Antenne Bayern", "Bayern 3", "SWR3", "Radio Hamburg", "Deutschlandfunk"]),
        .init(id: "gr", displayName: "Greece", apiName: "Greece", topBrands: ["Skai", "Athens Voice", "Sfera", "Derti", "Real FM"]),
        .init(id: "hu", displayName: "Hungary", apiName: "Hungary", topBrands: ["Petőfi Radio", "Retro Radio", "Radio 1", "Kossuth Radio"]),
        .init(id: "is", displayName: "Iceland", apiName: "Iceland", topBrands: ["RÚV Rás 1", "Bylgjan", "FM957", "RÚV Rás 2"]),
        .init(id: "in", displayName: "India", apiName: "India", topBrands: ["AIR FM Gold", "Radio Mirchi", "Red FM", "Big FM", "Radio City"]),
        .init(id: "id", displayName: "Indonesia", apiName: "Indonesia", topBrands: ["Prambors", "Hard Rock FM", "Trax FM", "Elshinta", "Smart FM"]),
        .init(id: "ie", displayName: "Ireland", apiName: "Ireland", topBrands: ["RTÉ Radio 1", "RTÉ 2FM", "Today FM", "Newstalk", "Spin", "Classic Hits"]),
        .init(id: "il", displayName: "Israel", apiName: "Israel", topBrands: ["Galei Tzahal", "Galgalatz", "Kan 88", "Reshet Bet", "Radio Tel Aviv"]),
        .init(id: "it", displayName: "Italy", apiName: "Italy", topBrands: ["RTL 102.5", "Radio Deejay", "RDS", "Radio Italia", "Radio 105", "Virgin Radio Italia", "RAI Radio 1"]),
        .init(id: "jp", displayName: "Japan", apiName: "Japan", topBrands: ["NHK Radio 1", "J-WAVE", "TOKYO FM", "TBS Radio", "Nippon Broadcasting"]),
        .init(id: "ke", displayName: "Kenya", apiName: "Kenya", topBrands: ["Capital FM", "Radio Jambo", "Kiss FM", "Classic 105", "Nation FM"]),
        .init(id: "kr", displayName: "South Korea", apiName: "Korea, Republic of", topBrands: ["KBS", "SBS Power FM", "MBC FM4U", "Arirang Radio"]),
        .init(id: "lu", displayName: "Luxembourg", apiName: "Luxembourg", topBrands: ["RTL", "Eldoradio", "Radio 100,7"]),
        .init(id: "my", displayName: "Malaysia", apiName: "Malaysia", topBrands: ["HITZ", "ERA", "Lite", "MIX", "Sinar"]),
        .init(id: "mx", displayName: "Mexico", apiName: "Mexico", topBrands: ["Los 40", "W Radio", "Exa FM", "Radio Fórmula", "Imagen Radio"]),
        .init(id: "nl", displayName: "Netherlands", apiName: "Netherlands", topBrands: ["NPO Radio 1", "NPO Radio 2", "Radio 538", "Sky Radio", "Qmusic", "3FM"]),
        .init(id: "nz", displayName: "New Zealand", apiName: "New Zealand", topBrands: ["ZM", "The Edge", "Newstalk ZB", "The Rock", "More FM", "RNZ National"]),
        .init(id: "ng", displayName: "Nigeria", apiName: "Nigeria", topBrands: ["Cool FM", "Wazobia FM", "Classic FM", "Beat FM", "Brila FM"]),
        .init(id: "no", displayName: "Norway", apiName: "Norway", topBrands: ["NRK P1", "NRK P3", "P4", "Radio Norge", "NRJ Norway"]),
        .init(id: "pk", displayName: "Pakistan", apiName: "Pakistan", topBrands: ["FM 100", "City FM 89", "FM 103", "Radio Pakistan", "Power 99"]),
        .init(id: "pe", displayName: "Peru", apiName: "Peru", topBrands: ["RPP", "Radio Moda", "Studio 92", "Panamericana", "La Karibeña"]),
        .init(id: "ph", displayName: "Philippines", apiName: "Philippines", topBrands: ["Wish 107.5", "RX 93.1", "Monster RX", "Love Radio", "DWIZ"]),
        .init(id: "pl", displayName: "Poland", apiName: "Poland", topBrands: ["RMF FM", "Radio ZET", "Polskie Radio", "Eska", "TOK FM"]),
        .init(id: "pt", displayName: "Portugal", apiName: "Portugal", topBrands: ["RFM", "Rádio Comercial", "Antena 1", "TSF", "M80"]),
        .init(id: "ro", displayName: "Romania", apiName: "Romania", topBrands: ["Radio ZU", "Kiss FM", "Europa FM", "Digi FM", "Radio România Actualități"]),
        .init(id: "sa", displayName: "Saudi Arabia", apiName: "Saudi Arabia", topBrands: ["MBC FM", "Rotana FM", "Mix FM", "UFM", "Alif Alif"]),
        .init(id: "sg", displayName: "Singapore", apiName: "Singapore", topBrands: ["CNA938", "Class 95", "987", "Kiss92", "Gold 905"]),
        .init(id: "sk", displayName: "Slovakia", apiName: "Slovakia", topBrands: ["Radio Slovakia", "Fun Radio", "Europa 2", "Radio Expres"]),
        .init(id: "si", displayName: "Slovenia", apiName: "Slovenia", topBrands: ["Val 202", "Radio 1", "Radio City", "Radio Aktual"]),
        .init(id: "za", displayName: "South Africa", apiName: "South Africa", topBrands: ["Metro FM", "5FM", "947", "Kaya 959", "702"]),
        .init(id: "es", displayName: "Spain", apiName: "Spain", topBrands: ["Cadena SER", "COPE", "Los 40", "Onda Cero", "RNE", "Kiss FM", "Europa FM"]),
        .init(id: "se", displayName: "Sweden", apiName: "Sweden", topBrands: ["Sveriges Radio P1", "P3", "Mix Megapol", "RIX FM", "NRJ Sweden"]),
        .init(id: "ch", displayName: "Switzerland", apiName: "Switzerland", topBrands: ["SRF 1", "SRF 3", "Radio 24", "Radio Energy", "Couleur 3"]),
        .init(id: "th", displayName: "Thailand", apiName: "Thailand", topBrands: ["Eazy FM", "Cool Fahrenheit", "Green Wave", "MCOT", "Flex 104.5"]),
        .init(id: "tr", displayName: "Turkey", apiName: "Turkey", topBrands: ["Kral FM", "Power FM", "SlowTürk", "Radyo D", "TRT FM"]),
        .init(id: "ae", displayName: "United Arab Emirates", apiName: "United Arab Emirates", topBrands: ["Dubai 92", "Virgin Radio Dubai", "Radio 2", "FM 92", "Al Arabiya"]),
        .init(id: "uk", displayName: "United Kingdom", apiName: "United Kingdom", topBrands: ["BBC Radio 1", "BBC Radio 2", "BBC Radio 4", "BBC 6 Music", "Capital", "Virgin Radio UK", "Heart", "Classic FM", "LBC", "talkSPORT"]),
        .init(id: "us", displayName: "United States", apiName: "United States", topBrands: ["NPR", "KIIS", "Z100", "KEXP", "Hot 97", "iHeart", "1010 WINS", "WNYC", "KROQ", "SiriusXM"]),
        .init(id: "uy", displayName: "Uruguay", apiName: "Uruguay", topBrands: ["El Espectador", "Del Sol", "Monte Carlo", "Oceano FM", "Radio Carve"]),
        .init(id: "ve", displayName: "Venezuela", apiName: "Venezuela", topBrands: ["Circuito Onda", "Éxitos", "La Mega", "Onda", "Unión Radio"]),
        .init(id: "vn", displayName: "Vietnam", apiName: "Vietnam", topBrands: ["VOV1", "VOV Giao Thông", "VOH", "Xone FM", "VOV2"])
    ]

    static let all: [CountryPreset] = base.sorted {
        $0.displayName.localizedCaseInsensitiveCompare($1.displayName) == .orderedAscending
    }
}
