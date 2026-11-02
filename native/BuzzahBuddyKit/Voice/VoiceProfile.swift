import AppIntents

/// The profiles a phrase can name; raw values are the firmware profile ids. Never Quick Test (6).
@available(iOS 16.0, *)
enum VoiceProfile: Int, AppEnum {
    case regular = 1
    case noisy = 2
    case hybrid = 3
    case custom = 4
    case gentle = 5

    static let typeDisplayRepresentation: TypeDisplayRepresentation = "Profile"

    static let caseDisplayRepresentations: [VoiceProfile: DisplayRepresentation] = [
        .regular: "Regular",
        .noisy: "Noisy",
        .hybrid: "Hybrid",
        .custom: "Custom",
        .gentle: "Gentle",
    ]
}
