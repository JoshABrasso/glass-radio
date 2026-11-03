// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "GlassRadio",
    platforms: [
        .macOS(.v13)
    ],
    products: [
        .executable(name: "GlassRadio", targets: ["RadioGlass"])
    ],
    targets: [
        .executableTarget(
            name: "RadioGlass",
            path: "Sources/RadioGlass"
        )
    ]
)
