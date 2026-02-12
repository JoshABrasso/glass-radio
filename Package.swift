// swift-tools-version: 5.10
import PackageDescription

let package = Package(
    name: "GlassRadio",
    platforms: [
        .macOS(.v14)
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
