// swift-tools-version: 5.10
import PackageDescription

let package = Package(
    name: "BambuDry",
    platforms: [.macOS(.v13)],
    products: [
        .library(name: "BambuDryCore", targets: ["BambuDryCore"]),
        .executable(name: "bambudry-cli", targets: ["BambuDryCLI"]),
    ],
    dependencies: [
        .package(url: "https://github.com/emqx/CocoaMQTT.git", from: "2.1.0"),
    ],
    targets: [
        .target(
            name: "BambuDryCore",
            dependencies: [
                .product(name: "CocoaMQTT", package: "CocoaMQTT"),
            ]
        ),
        .executableTarget(
            name: "BambuDryCLI",
            dependencies: ["BambuDryCore"]
        ),
        .testTarget(
            name: "BambuDryCoreTests",
            dependencies: ["BambuDryCore"],
            resources: [.process("Fixtures")]
        ),
    ]
)
