import AppIntents

// \(.applicationName) also matches INAlternativeAppNames in the app's Info.plist:
// Glove, Gloves and BlueBuzzah.
@available(iOS 16.0, *)
struct BuzzahBuddyShortcuts: AppShortcutsProvider {
    static var appShortcuts: [AppShortcut] {
        AppShortcut(
            intent: StartSessionIntent(),
            phrases: [
                "Start \(.applicationName) session",
                "Start \(.applicationName) \(\.$profile) session",
            ],
            shortTitle: "Start Session",
            systemImageName: "play.fill"
        )
        AppShortcut(
            intent: PauseSessionIntent(),
            phrases: ["Pause \(.applicationName) session"],
            shortTitle: "Pause Session",
            systemImageName: "pause.fill"
        )
        AppShortcut(
            intent: ResumeSessionIntent(),
            phrases: ["Resume \(.applicationName) session"],
            shortTitle: "Resume Session",
            systemImageName: "playpause.fill"
        )
        AppShortcut(
            intent: StopSessionIntent(),
            phrases: ["Stop \(.applicationName) session"],
            shortTitle: "Stop Session",
            systemImageName: "stop.fill"
        )
        AppShortcut(
            intent: CheckBatteryIntent(),
            phrases: ["Check \(.applicationName) battery"],
            shortTitle: "Check Battery",
            systemImageName: "battery.75percent"
        )
        AppShortcut(
            intent: CheckProfileIntent(),
            phrases: ["Check \(.applicationName) profile"],
            shortTitle: "Check Profile",
            systemImageName: "slider.horizontal.3"
        )
    }
}
