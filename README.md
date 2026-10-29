# BuzzahBuddy

## Overview

BuzzahBuddy is a cross-platform mobile companion app for BlueBuzzah, open-source vibrotactile gloves for people living with Parkinson's. It connects to the gloves over Bluetooth Low Energy (BLE) to run sessions, change settings and test the motors.

## Important

BlueBuzzah devices are not FDA approved medical devices and not intended to diagnose, treat, cure, or prevent any disease. The information provided through BlueBuzzah is for general wellness purposes only and not a substitute for professional medical advice, diagnosis, or treatment.

Important: People with implanted electrodes from deep brain stimulation (DBS), heart pacemakers, or other internal electronic devices should take special precautions around all electronic devices, including BlueBuzzah. Consult your physician before use.

**Mission:** Create a highly accessible, intuitive mobile app optimized for users with Parkinson's disease, featuring tremor-friendly UI and comprehensive assistive technology support.

## Related Resources

- **BlueBuzzah Project:** [HealthUnlocked Community Post](https://healthunlocked.com/cure-parkinsons/posts/151962393/the-blue-buzzah-a-new-wireless-diy-vibrotactile-glove)
- **Firmware Repository:** [BlueBuzzah-Firmware on GitHub](https://github.com/BlueBuzzah/BlueBuzzah-Firmware)
- **Hardware Repository:** [BlueBuzzah-Hardware on GitHub](https://github.com/BlueBuzzah/BlueBuzzah-Hardware)

## Technology Stack

- **.NET 10** and **.NET MAUI**, C#
- **MVVM** with CommunityToolkit.Mvvm
- **Bluetooth Low Energy** via Plugin.BLE
- **Testing:** xUnit

Target platforms: **iOS** 15.0+ and **Android** API 24+ (Android 7.0), targeting API 36 (Android 16).

## Prerequisites

- **.NET 10 SDK** ([download](https://dotnet.microsoft.com/download))
- **MAUI workloads:** `dotnet workload install maui-android maui-ios`
- **Xcode** (macOS, for iOS) in a version the installed `maui-ios` workload supports. CI pins the two together in `.github/workflows/`.
- **Android SDK** (API 36 to build; API 24+ to run)
- **make** (preinstalled on macOS)

iOS builds need a Mac. On Windows only the Android app builds.

## Setup

```bash
git clone https://github.com/BlueBuzzah/BuzzahBuddy
cd BuzzahBuddy
```

## Common Commands

| Command | What it does |
|---------|--------------|
| `make test` | Runs the unit tests (`BuzzahBuddy.Tests`) |
| `make build` | Builds every target (needs both workloads) |
| `make start:ios` | Builds and launches on the default iOS Simulator, then returns |
| `make start:android` | Builds and launches on the running Android emulator or connected device, then returns |

The iOS Simulator may start without a window. Open it from Xcode's DeviceHub (Xcode 27+) or the Simulator app.

### Building one platform

```bash
dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net10.0-ios -p:TargetFrameworks=net10.0-ios
dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net10.0-android -p:TargetFrameworks=net10.0-android
```

Keep `-p:TargetFrameworks`: without it, restore resolves every target and fails (NETSDK1147) wherever a workload isn't installed.

## Troubleshooting

- **No simulator or emulator found:** `xcrun simctl list devices` (iOS), `adb devices` (Android; `adb kill-server && adb start-server` if it's stuck).
- **Stale build output:** `dotnet clean`, then build again with `--no-incremental`.
- **Device logs:** Console.app or Xcode's Devices window (iOS), `adb logcat` (Android).
- **Detailed test output:** `dotnet test BuzzahBuddy.Tests --logger "console;verbosity=detailed"`.

## Accessibility

This app is designed with accessibility as a core requirement, following the BlueBuzzah design system (in the [BlueBuzzah-Workspace](https://github.com/BlueBuzzah/BlueBuzzah-Workspace) repo):

- **WCAG 2.2 Level AA** compliance
- **Large touch targets** (48x48pt minimum)
- **High contrast ratios** (4.5:1 for text, 7:1 in high-contrast themes)
- **Text scaling** to the largest iOS Dynamic Type and Android font sizes
- **Semantic properties** for screen readers (VoiceOver, TalkBack)
- **Tremor-friendly UI** optimized for Parkinson's patients

## License

BuzzahBuddy is [MIT licensed](./LICENSE).

## Contributing

1. Fork it!
2. Create your feature branch: `git checkout -b my-new-feature`
3. Commit your changes: `git commit -am 'Added some new feature'`
4. Push to the branch: `git push origin my-new-feature`
5. [Submit a pull request!](https://github.com/BlueBuzzah/BuzzahBuddy/compare)
