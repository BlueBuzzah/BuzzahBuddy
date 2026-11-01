import Foundation

/// Implemented in C# (BuzzahBuddy.Native.Binding.VoiceCommandProvider) and registered in
/// AppDelegate.FinishedLaunching, so every intent's perform() runs C# in the app's process.
@objc(BBVoiceCommandProvider) public protocol VoiceCommandProvider: AnyObject {
    /// `command` is one of start, pause, resume, stop, battery, profile; `profileId` 0 means none.
    /// `completion` gets the sentence Siri speaks.
    @objc(run:profileId:completion:)
    func run(_ command: String, profileId: Int, completion: @escaping (String) -> Void)

    /// Nil means "ask for confirmation"; a string is the reply to speak instead.
    @objc(stopPrecheckWithCompletion:)
    func stopPrecheck(completion: @escaping (String?) -> Void)
}

@objc(BBVoice) public final class VoiceBridge: NSObject {
    @objc public static let shared = VoiceBridge()

    @objc public var provider: VoiceCommandProvider?

    private override init() {
        super.init()
    }
}

enum VoiceRouter {
    static let notConnected = "Your gloves aren't connected."

    static func run(_ command: String, profileId: Int = 0) async -> String {
        guard let provider = VoiceBridge.shared.provider else { return notConnected }
        return await withCheckedContinuation { continuation in
            provider.run(command, profileId: profileId) { continuation.resume(returning: $0) }
        }
    }

    static func stopPrecheck() async -> String? {
        guard let provider = VoiceBridge.shared.provider else { return notConnected }
        return await withCheckedContinuation { continuation in
            provider.stopPrecheck { continuation.resume(returning: $0) }
        }
    }
}
