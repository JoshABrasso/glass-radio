// swift-tools-version: 5.10
import PackageDescription

let package = Package(
    name: "RadioGlass",
    platforms: [
        .macOS(.v14)
    ],
    products: [
        .executable(name: "RadioGlass", targets: ["RadioGlass"])
    ],
    targets: [
        .executableTarget(
            name: "RadioGlass",
            path: "Sources/RadioGlass"
        )
    ]
)
