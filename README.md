![BuzzahBuddy](readme-logo.svg)

## Overview

BuzzahBuddy is a cross-platform mobile companion app for the BlueBuzzah tactile gloves, a vibrotactile therapeutic device for Parkinson's disease treatment. The app connects to BlueBuzzah gloves via Bluetooth Low Energy (BLE) to control vibration patterns, track therapy usage, and manage treatment sessions.

**Mission:** Create a highly accessible, intuitive mobile app optimized for users with Parkinson's disease, featuring tremor-friendly UI and comprehensive assistive technology support.

## Related Resources

- **BlueBuzzah Project:** [HealthUnlocked Community Post](https://healthunlocked.com/cure-parkinsons/posts/151962393/the-blue-buzzah-a-new-wireless-diy-vibrotactile-glove)
- **Hardware Repository:** [BlueBuzzah Gloves on GitHub](https://github.com/PWPInnovator898/BlueBuzzah-Gloves)

## Technology Stack

- **.NET 9.0** with C# 13
- **.NET MAUI** (Multi-platform App UI)
- **MVVM Architecture** with CommunityToolkit.Mvvm
- **Bluetooth Low Energy** via Plugin.BLE
- **Testing:** xUnit with Moq

### Target Platforms

- **iOS** 15.0+
- **Android** API 21+

### Key Dependencies

- `Microsoft.Maui.Controls` - Cross-platform UI framework
- `Plugin.BLE` (3.1.0) - Bluetooth Low Energy communication
- `CommunityToolkit.Mvvm` (8.3.2) - MVVM helpers and source generators
- `xunit` (2.9.2) - Unit testing framework
- `Moq` (4.20.72) - Mocking framework for tests

## Prerequisites

- **.NET 9.0 SDK** or later
- **Visual Studio 2022** (Windows/Mac) or **VS Code** with C# DevKit
- **Xcode** (for iOS development on macOS)
- **Android SDK** (API 21+)

### Development Environment Setup

**macOS:**

```bash
# Install .NET 9.0 SDK
brew install dotnet

# Ensure Xcode Command Line Tools are installed
xcode-select --install
```

**Windows:**

- Install Visual Studio 2022 with .NET MAUI workload
- Install Android SDK via Visual Studio Installer

## Setup

1. **Clone the repository**

   ```bash
   git clone https://github.com/bluebuzzah/buzzahbuddy
   cd BuzzahBuddy
   ```

2. **Restore dependencies**

   ```bash
   dotnet restore
   ```

3. **Verify MAUI workloads**
   ```bash
   dotnet workload install maui
   ```

## Build

### iOS

```bash
dotnet build -f net9.0-ios
```

### Android

```bash
dotnet build -f net9.0-android
```

### All Platforms

```bash
dotnet build
```

## Running the App

### iOS Simulator

```bash
dotnet build -f net9.0-ios -t:Run
```

### Android Emulator

```bash
dotnet build -f net9.0-android -t:Run
```

### Using Visual Studio

1. Open `BuzzahBuddy.slnx`
2. Select target platform (iOS/Android)
3. Choose device/emulator from dropdown
4. Press F5 or click Run

## Debugging

- **Breakpoints:** Set breakpoints in C# code and XAML code-behind
- **Live Visual Tree:** Inspect XAML hierarchy during runtime (Visual Studio)
- **XAML Hot Reload:** Modify XAML and see changes instantly without rebuilding
- **Device Logs:**
  - iOS: Use Console.app or Xcode Devices window
  - Android: Use `adb logcat` or Android Studio Logcat

### Common Issues

**iOS Simulator Not Found:**

```bash
# List available simulators
xcrun simctl list
```

**Android Emulator Issues:**

```bash
# List Android devices
adb devices

# Restart ADB server
adb kill-server && adb start-server
```

**Build Errors:**

```bash
# Clean build artifacts
dotnet clean
dotnet build --no-incremental
```

## Testing

```bash
# Run all tests
dotnet test

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"
```

## Accessibility

This app is designed with accessibility as a core requirement:

- **WCAG 2.1 Level AA** compliance
- **Large touch targets** (48x48pt minimum)
- **High contrast ratios** (4.5:1 for text)
- **Semantic properties** for screen readers (VoiceOver, TalkBack)
- **Tremor-friendly UI** optimized for Parkinson's patients

## License

The BlueBuzzah project is see [MIT licensed](./LICENSE).

## Contributing

1. Fork it!
2. Create your feature branch: `git checkout -b my-new-feature`
3. Commit your changes: `git commit -am 'Added some new feature'`
4. Push to the branch: `git push origin my-new-feature`
5. [Submit a pull request!](https://github.com/bluebuzzah/bluebuzzah/pull/new/development)
