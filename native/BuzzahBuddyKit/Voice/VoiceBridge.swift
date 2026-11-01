import Foundation

/// Implemented in C# (BuzzahBuddy.Native.Binding.VoiceCommandProvider) and registered in
/// AppDelegate.FinishedLaunching, so every intent's perform() runs C# in the app's process.
@objc(BBVoiceCommandProvider) public protocol VoiceCommandProvider: AnyObject {
    /// `command` is one of start, pause, resume, stop, battery, profile; `profileId` 0 means none.
    /// `completion` gets the sentence Siri speaks. Called off the main thread; `completion` may be
    /// called from any thread.
    @objc(run:profileId:completion:)
    func run(_ command: String, profileId: Int, completion: @escaping (String) -> Void)

    /// Nil means "ask for confirmation"; a string is the reply to speak instead. Called off the
    /// main thread; `completion` may be called from any thread.
    @objc(stopPrecheckWithCompletion:)
    func stopPrecheck(completion: @escaping (String?) -> Void)
}

@objc(BBVoice) public final class VoiceBridge: NSObject {
    @objc public static let shared = VoiceBridge()

    /// Written once in AppDelegate.FinishedLaunching, before any intent can run, so it needs no lock.
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
            let once = ResumeOnce(continuation, label: "run \(command)")
            provider.run(command, profileId: profileId) { once.resume($0) }
        }
    }

    static func stopPrecheck() async -> String? {
        guard let provider = VoiceBridge.shared.provider else { return notConnected }
        return await withCheckedContinuation { continuation in
            let once = ResumeOnce(continuation, label: "stopPrecheck")
            provider.stopPrecheck { once.resume($0) }
        }
    }
}

/// Resumes its continuation on the first call only. A second resume of a checked continuation
/// crashes the app, so a provider that calls back twice is logged and ignored instead.
private final class ResumeOnce<T>: @unchecked Sendable {
    private let lock = NSLock()
    private var continuation: CheckedContinuation<T, Never>?
    private let label: String

    init(_ continuation: CheckedContinuation<T, Never>, label: String) {
        self.continuation = continuation
        self.label = label
    }

    func resume(_ value: T) {
        lock.lock()
        let pending = continuation
        continuation = nil
        lock.unlock()
        guard let pending else {
            NSLog("[VOICE] %@ completion called again; ignored", label)
            return
        }
        pending.resume(returning: value)
    }
}
