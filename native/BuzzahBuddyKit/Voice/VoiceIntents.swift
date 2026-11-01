import AppIntents

// Every intent answers in the background: supportedModes on iOS 26+, openAppWhenRun before it.
// The reply always comes from C# through VoiceRouter.

@available(iOS 16.0, *)
struct StartSessionIntent: AppIntent {
    static let title: LocalizedStringResource = "Start Session"
    static var openAppWhenRun: Bool { false }
    @available(iOS 26.0, *)
    static var supportedModes: IntentModes { .background }

    /// Nil (no profile named, or Siri didn't catch it) is a plain Start, never an error.
    @Parameter(title: "Profile")
    var profile: VoiceProfile?

    func perform() async throws -> some IntentResult & ProvidesDialog {
        let reply = await VoiceRouter.run("start", profileId: profile?.rawValue ?? 0)
        return .result(dialog: "\(reply)")
    }
}

@available(iOS 16.0, *)
struct PauseSessionIntent: AppIntent {
    static let title: LocalizedStringResource = "Pause Session"
    static var openAppWhenRun: Bool { false }
    @available(iOS 26.0, *)
    static var supportedModes: IntentModes { .background }

    func perform() async throws -> some IntentResult & ProvidesDialog {
        let reply = await VoiceRouter.run("pause")
        return .result(dialog: "\(reply)")
    }
}

@available(iOS 16.0, *)
struct ResumeSessionIntent: AppIntent {
    static let title: LocalizedStringResource = "Resume Session"
    static var openAppWhenRun: Bool { false }
    @available(iOS 26.0, *)
    static var supportedModes: IntentModes { .background }

    func perform() async throws -> some IntentResult & ProvidesDialog {
        let reply = await VoiceRouter.run("resume")
        return .result(dialog: "\(reply)")
    }
}

@available(iOS 16.0, *)
struct StopSessionIntent: AppIntent {
    static let title: LocalizedStringResource = "Stop Session"
    static var openAppWhenRun: Bool { false }
    @available(iOS 26.0, *)
    static var supportedModes: IntentModes { .background }

    func perform() async throws -> some IntentResult & ProvidesDialog {
        // A reply here (not connected, no session) skips the question.
        if let reply = await VoiceRouter.stopPrecheck() {
            return .result(dialog: "\(reply)")
        }
        do {
            if #available(iOS 18.0, *) {
                try await requestConfirmation(dialog: "End your session?")
            } else {
                try await requestConfirmation(result: .result(dialog: "End your session?"))
            }
        } catch {
            // A "no" throws; so does anything else. Either way, the session keeps going.
            return .result(dialog: "Okay, your session keeps going.")
        }
        let reply = await VoiceRouter.run("stop")
        return .result(dialog: "\(reply)")
    }
}

@available(iOS 16.0, *)
struct CheckBatteryIntent: AppIntent {
    static let title: LocalizedStringResource = "Check Battery"
    static var openAppWhenRun: Bool { false }
    @available(iOS 26.0, *)
    static var supportedModes: IntentModes { .background }

    func perform() async throws -> some IntentResult & ProvidesDialog {
        let reply = await VoiceRouter.run("battery")
        return .result(dialog: "\(reply)")
    }
}

@available(iOS 16.0, *)
struct CheckProfileIntent: AppIntent {
    static let title: LocalizedStringResource = "Check Profile"
    static var openAppWhenRun: Bool { false }
    @available(iOS 26.0, *)
    static var supportedModes: IntentModes { .background }

    func perform() async throws -> some IntentResult & ProvidesDialog {
        let reply = await VoiceRouter.run("profile")
        return .result(dialog: "\(reply)")
    }
}
