namespace GlassRadio.Domain;

// Mac-derived country metadata. TopBrands are network-recognition hints used
// by curation and targeted catalogue enrichment; they are not station records,
// a fixed presentation order, or a country-specific station catalogue.
public sealed record CountryPreset(
    string Id,
    string DisplayName,
    string ApiName,
    IReadOnlyList<string> TopBrands
)
{
    public string CountryCode => Id == "uk" ? "GB" : Id.ToUpperInvariant();

    public string FlagEmoji => string.Concat(CountryCode
        .ToUpperInvariant()
        .Where(static character => character is >= 'A' and <= 'Z')
        .Select(static character => char.ConvertFromUtf32(0x1F1E6 + character - 'A')));

    // Windows does not reliably render regional-indicator emoji pairs as flag glyphs (most render
    // as a two-letter code instead - confirmed visually, not assumed), so UI uses the same
    // high-resolution FlagCDN source as the Mac Explore page. Keep the small bundled image as a
    // visible fallback for offline/error states; the Explore artwork itself is never upscaled from
    // the old 80px placeholder.
    public string FlagImagePath => $"https://flagcdn.com/w640/{CountryCode.ToLowerInvariant()}.png";
    public string FlagFallbackImagePath => $"/Assets/Flags/{CountryCode.ToLowerInvariant()}.png";

    private static readonly CountryPreset[] Base =
    [
        new("ar", "Argentina",       "Argentina",           ["Radio Mitre","La 100","Cadena 3","Continental","Metro"]),
        new("au", "Australia",       "Australia",           ["triple j","ABC Radio","Nova","KIIS","2GB","3AW","Smooth FM","Gold 104.3"]),
        new("at", "Austria",         "Austria",             ["Hitradio Ö3","FM4","Kronehit","Radio Wien","Antenne Steiermark"]),
        new("be", "Belgium",         "Belgium",             ["Radio 2","Qmusic","Studio Brussel","MNM","Bel RTL","Nostalgie"]),
        new("br", "Brazil",          "Brazil",              ["Jovem Pan","CBN","BandNews FM","Antena 1","Transamérica"]),
        new("bg", "Bulgaria",        "Bulgaria",            ["Darik Radio","Radio 1","NRJ Bulgaria","BNT Radio"]),
        new("ca", "Canada",          "Canada",              ["CBC Radio One","CBC Music","Virgin Radio","CHUM","CFOX","CHFI","Boom","98.1 CHFI"]),
        new("cl", "Chile",           "Chile",               ["Radio Cooperativa","Bio Bio","ADN Radio","Radio Carolina","Rock & Pop"]),
        new("co", "Colombia",        "Colombia",            ["Caracol Radio","W Radio","La FM","Blu Radio","RCN Radio"]),
        new("hr", "Croatia",         "Croatia",             ["Otvoreni","Radio Dalmacija","HR1","HR2","Narodni"]),
        new("cz", "Czechia",         "Czech Republic",      ["Radiožurnál","Evropa 2","Frekvence 1","ČRo Plus","Impuls"]),
        new("dk", "Denmark",         "Denmark",             ["DR P1","DR P3","NOVA","The Voice","Radio4"]),
        new("eg", "Egypt",           "Egypt",               ["Nogoum FM","Nile FM","Radio Masr","Mega FM","Hits FM"]),
        new("fi", "Finland",         "Finland",             ["Yle Radio Suomi","YleX","Radio Nova","NRJ Finland","SuomiPop"]),
        new("fr", "France",          "France",              ["France Inter","RTL","NRJ","Europe 1","RMC","Skyrock","France Info","Nostalgie"]),
        new("de", "Germany",         "Germany",             ["1LIVE","WDR","NDR 2","Antenne Bayern","Bayern 3","SWR3","Radio Hamburg","Deutschlandfunk"]),
        new("gr", "Greece",          "Greece",              ["Skai","Athens Voice","Sfera","Derti","Real FM"]),
        new("hu", "Hungary",         "Hungary",             ["Petőfi Radio","Retro Radio","Radio 1","Kossuth Radio"]),
        new("is", "Iceland",         "Iceland",             ["RÚV Rás 1","Bylgjan","FM957","RÚV Rás 2"]),
        new("in", "India",           "India",               ["AIR FM Gold","Radio Mirchi","Red FM","Big FM","Radio City"]),
        new("id", "Indonesia",       "Indonesia",           ["Prambors","Hard Rock FM","Trax FM","Elshinta","Smart FM"]),
        new("ie", "Ireland",         "Ireland",             ["RTÉ Radio 1","RTÉ 2FM","Today FM","Newstalk","Spin","Classic Hits"]),
        new("il", "Israel",          "Israel",              ["Galei Tzahal","Galgalatz","Kan 88","Reshet Bet","Radio Tel Aviv"]),
        new("it", "Italy",           "Italy",               ["RTL 102.5","Radio Deejay","RDS","Radio Italia","Radio 105","Virgin Radio Italia","RAI Radio 1"]),
        new("jp", "Japan",           "Japan",               ["NHK Radio 1","J-WAVE","TOKYO FM","TBS Radio","Nippon Broadcasting"]),
        new("ke", "Kenya",           "Kenya",               ["Capital FM","Radio Jambo","Kiss FM","Classic 105","Nation FM"]),
        new("kr", "South Korea",     "Korea, Republic of",  ["KBS","SBS Power FM","MBC FM4U","Arirang Radio"]),
        new("lu", "Luxembourg",      "Luxembourg",          ["RTL","Eldoradio","Radio 100,7"]),
        new("my", "Malaysia",        "Malaysia",            ["HITZ","ERA","Lite","MIX","Sinar"]),
        new("mx", "Mexico",          "Mexico",              ["Los 40","W Radio","Exa FM","Radio Fórmula","Imagen Radio"]),
        new("nl", "Netherlands",     "Netherlands",         ["NPO Radio 1","NPO Radio 2","Radio 538","Sky Radio","Qmusic","3FM"]),
        new("nz", "New Zealand",     "New Zealand",         ["ZM","The Edge","Newstalk ZB","The Rock","More FM","RNZ National"]),
        new("ng", "Nigeria",         "Nigeria",             ["Cool FM","Wazobia FM","Classic FM","Beat FM","Brila FM"]),
        new("no", "Norway",          "Norway",              ["NRK P1","NRK P3","P4","Radio Norge","NRJ Norway"]),
        new("pk", "Pakistan",        "Pakistan",            ["FM 100","City FM 89","FM 103","Radio Pakistan","Power 99"]),
        new("pe", "Peru",            "Peru",                ["RPP","Radio Moda","Studio 92","Panamericana","La Karibeña"]),
        new("ph", "Philippines",     "Philippines",         ["Wish 107.5","RX 93.1","Monster RX","Love Radio","DWIZ"]),
        new("pl", "Poland",          "Poland",              ["RMF FM","Radio ZET","Polskie Radio","Eska","TOK FM"]),
        new("pt", "Portugal",        "Portugal",            ["RFM","Rádio Comercial","Antena 1","TSF","M80"]),
        new("ro", "Romania",         "Romania",             ["Radio ZU","Kiss FM","Europa FM","Digi FM","Radio România Actualități"]),
        new("sa", "Saudi Arabia",    "Saudi Arabia",        ["MBC FM","Rotana FM","Mix FM","UFM","Alif Alif"]),
        new("sg", "Singapore",       "Singapore",           ["CNA938","Class 95","987","Kiss92","Gold 905"]),
        new("sk", "Slovakia",        "Slovakia",            ["Radio Slovakia","Fun Radio","Europa 2","Radio Expres"]),
        new("si", "Slovenia",        "Slovenia",            ["Val 202","Radio 1","Radio City","Radio Aktual"]),
        new("za", "South Africa",    "South Africa",        ["Metro FM","5FM","947","Kaya 959","702"]),
        new("es", "Spain",           "Spain",               ["Cadena SER","COPE","Los 40","Onda Cero","RNE","Kiss FM","Europa FM"]),
        new("se", "Sweden",          "Sweden",              ["Sveriges Radio P1","P3","Mix Megapol","RIX FM","NRJ Sweden"]),
        new("ch", "Switzerland",     "Switzerland",         ["SRF 1","SRF 3","Radio 24","Radio Energy","Couleur 3"]),
        new("th", "Thailand",        "Thailand",            ["Eazy FM","Cool Fahrenheit","Green Wave","MCOT","Flex 104.5"]),
        new("tr", "Turkey",          "Turkey",              ["Kral FM","Power FM","SlowTürk","Radyo D","TRT FM"]),
        new("ae", "United Arab Emirates","United Arab Emirates",["Dubai 92","Virgin Radio Dubai","Radio 2","FM 92","Al Arabiya"]),
        new("uk", "United Kingdom",  "United Kingdom",
        [
            "BBC Radio 1 Dance", "BBC Radio 1",
            "Capital Dance", "Capital Anthems", "Capital",
            "KISS Dance", "KISS",
            "Heart", "Radio X", "Absolute Radio",
            "BBC Radio 1Xtra", "BBC Radio 2", "BBC Radio 3",
            "BBC Radio 4 Extra", "BBC Radio 4",
            "BBC Radio 5 Live Sports Extra", "BBC Radio 5 Live",
            "BBC Radio 6 Music", "Capital XTRA",
            "KISS XTRA", "Kisstory", "Heart Dance", "Heart Extra",
            "Classic FM", "Smooth Radio", "LBC", "Smash Hits Radio",
            "Hits Radio", "Absolute Radio 90s", "Magic Radio",
            "Greatest Hits Radio", "Jazz FM", "Scala Radio", "Kerrang",
            "Times Radio", "Talk Radio", "talkSPORT", "Virgin Radio UK",
            "Planet Rock", "Gold", "Boom Radio", "Fun Kids"
        ]),
        new("us", "United States",   "United States",       ["NPR","KIIS","Z100","KEXP","Hot 97","iHeart","1010 WINS","WNYC","KROQ","SiriusXM"]),
        new("uy", "Uruguay",         "Uruguay",             ["El Espectador","Del Sol","Monte Carlo","Oceano FM","Radio Carve"]),
        new("ve", "Venezuela",       "Venezuela",           ["Circuito Onda","Éxitos","La Mega","Onda","Unión Radio"]),
        new("vn", "Vietnam",         "Vietnam",             ["VOV1","VOV Giao Thông","VOH","Xone FM","VOV2"]),
    ];

    public static readonly IReadOnlyList<CountryPreset> All = Base
        .OrderBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();
}
