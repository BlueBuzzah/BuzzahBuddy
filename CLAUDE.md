# BuzzahBuddy - Project Guide

## Project Overview

**BuzzahBuddy** is a cross-platform mobile companion app for the BlueBuzzah tactile gloves, a vibrotactile therapeutic device for Parkinson's disease treatment. The app connects to upgraded BlueBuzzah gloves via Bluetooth Low Energy (BLE) to control vibration patterns, track usage, and manage therapy sessions.

### Key Links
- [BlueBuzzah HealthUnlocked](https://healthunlocked.com/cure-parkinsons/posts/151962393/the-blue-buzzah-a-new-wireless-diy-vibrotactile-glove)
- [BlueBuzzah GitHub Repository](https://github.com/PWPInnovator898/BlueBuzzah-Gloves)

### Mission
Create a **highly accessible**, intuitive mobile app optimized for users with Parkinson's disease, featuring tremor-friendly UI and comprehensive assistive technology support.

---

## Tech Stack

### Framework & Runtime
- **.NET 9.0** with C# 13
- **.NET MAUI** (Multi-platform App UI)
- Target Platforms: iOS 15+, Android 21+, MacCatalyst 15+

### Key Dependencies
```xml
<!-- Essential packages -->
<PackageReference Include="Microsoft.Maui.Controls" Version="$(MauiVersion)" />
<PackageReference Include="Plugin.BLE" Version="3.1.0" />
<PackageReference Include="CommunityToolkit.Mvvm" Version="8.3.2" /> <!-- Optional for source generators -->

<!-- Testing -->
<PackageReference Include="xunit" Version="2.9.2" />
<PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
<PackageReference Include="Moq" Version="4.20.72" />
```

### Project Configuration
- **Nullable Reference Types**: Enabled (`<Nullable>enable</Nullable>`)
- **Implicit Usings**: Enabled (`<ImplicitUsings>enable</ImplicitUsings>`)
- **Single Project**: True (all platforms in one .csproj)

---

## Architecture & Patterns

### MVVM Pattern
This project uses **Model-View-ViewModel (MVVM)** architecture:

```
┌──────────┐         ┌──────────────┐         ┌────────┐
│   View   │ ◄─────► │  ViewModel   │ ◄─────► │ Model  │
│  (XAML)  │         │ (INotify...) │         │ (Data) │
└──────────┘         └──────────────┘         └────────┘
                             │
                             ▼
                     ┌──────────────┐
                     │  Services    │
                     └──────────────┘
```

**Guidelines:**
- Views should contain **zero business logic** (only UI and data binding)
- ViewModels implement `INotifyPropertyChanged` (or use `ObservableObject` from CommunityToolkit)
- Models are pure data classes (DTOs, domain entities)
- Services contain business logic and external integrations (Bluetooth, storage)

### Dependency Injection
Use **built-in MAUI DI** via `MauiProgram.cs`:

```csharp
// Register services
builder.Services.AddSingleton<IBluetoothService, BluetoothService>();
builder.Services.AddTransient<MainPageViewModel>();

// Register pages with ViewModels
builder.Services.AddTransient<MainPage>();
```

**Service Lifetimes:**
- `AddSingleton`: Bluetooth, logging, app-wide state managers
- `AddTransient`: ViewModels, pages, short-lived services
- `AddScoped`: Not commonly used in MAUI (no request scope)

---

## Project Structure

Organize code using the following folder structure:

```
BuzzahBuddy/
├── Models/                  # Data models and entities
│   ├── GloveDevice.cs
│   ├── TherapySession.cs
│   └── VibrationPattern.cs
├── ViewModels/              # MVVM ViewModels
│   ├── BaseViewModel.cs
│   ├── MainPageViewModel.cs
│   ├── GloveControlViewModel.cs
│   └── SettingsViewModel.cs
├── Views/                   # XAML Pages and Controls
│   ├── MainPage.xaml
│   ├── GloveControlPage.xaml
│   └── Controls/
│       └── GloveStatusIndicator.xaml
├── Services/                # Business logic layer
│   ├── Bluetooth/
│   │   ├── IBluetoothService.cs
│   │   └── BluetoothService.cs
│   ├── Glove/
│   │   ├── IGloveControlService.cs
│   │   └── GloveControlService.cs
│   └── Storage/
│       └── IDataStorageService.cs
├── Resources/               # App resources
│   ├── Styles/             # Colors.xaml, Styles.xaml
│   ├── Images/             # Images and icons
│   └── Fonts/              # Custom fonts
├── Platforms/               # Platform-specific code
│   ├── Android/
│   ├── iOS/
│   └── MacCatalyst/
├── Converters/              # XAML value converters
├── Behaviors/               # XAML behaviors
└── MauiProgram.cs          # DI and app configuration
```

---

## Accessibility Requirements

BuzzahBuddy **MUST** be highly accessible for users with Parkinson's disease and motor impairments.

### WCAG 2.1 Level AA Compliance
- **Contrast Ratios**: 4.5:1 for normal text, 3:1 for large text
- **Focus Indicators**: Clear, visible focus states for keyboard/voice navigation
- **Text Sizing**: Support dynamic text scaling up to 200%

### Semantic Properties (Required)
Every interactive element **MUST** have semantic properties:

```xaml
<Button Text="Start Therapy"
        SemanticProperties.Description="Starts a new therapy session with connected gloves"
        SemanticProperties.Hint="Double tap to activate"
        AutomationId="StartTherapyButton" />

<Label Text="Session Duration"
       SemanticProperties.HeadingLevel="Level2" />
```

### Touch Targets
- **Minimum size**: 44x44 points (iOS HIG, Android Material)
- **Preferred size**: 48x48 points or larger
- **Spacing**: 8pt minimum between interactive elements

### Tremor-Friendly UI
- **No hover-only interactions** (unreliable with tremors)
- **Avoid small touch targets** and drag gestures
- **Confirmation dialogs** for destructive actions
- **Toggle buttons** preferred over sliders for binary choices
- **Large, high-contrast buttons** with clear labels

### Screen Reader Support
- Test with **VoiceOver** (iOS) and **TalkBack** (Android)
- Provide meaningful descriptions, not just visual labels
- Use `SemanticProperties.Description` for context-rich info

---

## Bluetooth Integration (Plugin.BLE)

### Overview
Use **Plugin.BLE** for cross-platform Bluetooth Low Energy communication with BlueBuzzah gloves.

### Service Architecture

```csharp
public interface IBluetoothService
{
    Task<IEnumerable<IDevice>> ScanForDevicesAsync(TimeSpan timeout);
    Task<bool> ConnectToDeviceAsync(IDevice device);
    Task DisconnectAsync();
    Task<bool> WriteCharacteristicAsync(Guid serviceId, Guid characteristicId, byte[] data);
    Task<byte[]> ReadCharacteristicAsync(Guid serviceId, Guid characteristicId);
    IObservable<ConnectionState> ConnectionStateChanged { get; }
}
```

### Implementation Patterns

**Device Discovery:**
```csharp
var adapter = CrossBluetoothLE.Current.Adapter;
adapter.ScanTimeout = 10000; // 10 seconds

var devices = new List<IDevice>();
adapter.DeviceDiscovered += (s, e) => devices.Add(e.Device);

await adapter.StartScanningForDevicesAsync();
```

**Connection Management:**
```csharp
await adapter.ConnectToDeviceAsync(device);

// Subscribe to disconnection events
device.WhenConnectionLost().Subscribe(args =>
{
    // Handle disconnection, attempt reconnect
});
```

**Characteristic Operations:**
```csharp
var service = await device.GetServiceAsync(SERVICE_UUID);
var characteristic = await service.GetCharacteristicAsync(CHAR_UUID);

// Write
await characteristic.WriteAsync(data);

// Read
var value = await characteristic.ReadAsync();

// Notify
characteristic.ValueUpdated += (s, e) =>
{
    // Handle notifications from glove
};
await characteristic.StartUpdatesAsync();
```

### Error Handling
Always wrap Bluetooth operations in try-catch:

```csharp
try
{
    await _bluetoothService.ConnectToDeviceAsync(device);
}
catch (DeviceConnectionException ex)
{
    // User-friendly error message
    await DisplayAlert("Connection Failed",
        "Could not connect to BlueBuzzah glove. Please ensure it's powered on.",
        "OK");
}
```

### UUIDs for BlueBuzzah Gloves
*(Update these based on actual glove firmware specifications)*

```csharp
public static class BlueBuzzahConstants
{
    public static readonly Guid ServiceUuid = Guid.Parse("..."); // Primary service
    public static readonly Guid VibrationControlCharacteristic = Guid.Parse("...");
    public static readonly Guid BatteryLevelCharacteristic = Guid.Parse("...");
    public static readonly Guid DeviceStatusCharacteristic = Guid.Parse("...");
}
```

---

## Code Standards

### .NET Naming Conventions
- **PascalCase**: Classes, methods, properties, public fields
  ```csharp
  public class GloveControlService
  public void StartVibration()
  public string DeviceName { get; set; }
  ```

- **camelCase**: Private fields, parameters, local variables
  ```csharp
  private readonly IBluetoothService _bluetoothService;
  public void Connect(string deviceId) { }
  ```

- **Interface Prefix**: Use `I` prefix for interfaces
  ```csharp
  public interface IBluetoothService { }
  ```

### Async/Await Patterns
- **Always** use `async`/`await` for I/O operations (Bluetooth, file, network)
- Suffix async methods with `Async`
  ```csharp
  public async Task<bool> ConnectToDeviceAsync(IDevice device)
  ```

- Use `ConfigureAwait(false)` in library code (not in UI ViewModels)
- Avoid `async void` except for event handlers

### Nullable Reference Types
- Enable nullable warnings as errors
- Use `?` for nullable types: `string? deviceName`
- Use `!` null-forgiving operator sparingly (only when you're certain)
- Initialize non-nullable properties in constructors

```csharp
public class GloveDevice
{
    public string Name { get; set; } = string.Empty; // Non-nullable
    public string? Manufacturer { get; set; }         // Nullable
}
```

### XML Documentation
Document all public APIs:

```csharp
/// <summary>
/// Connects to the specified BlueBuzzah glove device via Bluetooth.
/// </summary>
/// <param name="device">The BLE device to connect to.</param>
/// <returns>True if connection successful, false otherwise.</returns>
/// <exception cref="DeviceConnectionException">Thrown when connection fails.</exception>
public async Task<bool> ConnectToDeviceAsync(IDevice device)
```

### SOLID Principles
- **Single Responsibility**: Each class has one reason to change
- **Open/Closed**: Open for extension, closed for modification (use interfaces)
- **Liskov Substitution**: Derived classes must be substitutable for base classes
- **Interface Segregation**: Many specific interfaces > one general interface
- **Dependency Inversion**: Depend on abstractions, not concretions (use DI)

---

## Testing Strategy

### xUnit Framework
All tests use **xUnit** with the following structure:

```
BuzzahBuddy.Tests/
├── ViewModels/
│   ├── MainPageViewModelTests.cs
│   └── GloveControlViewModelTests.cs
├── Services/
│   ├── BluetoothServiceTests.cs
│   └── GloveControlServiceTests.cs
├── Mocks/
│   ├── MockBluetoothService.cs
│   └── MockGloveDevice.cs
└── TestHelpers/
    └── ViewModelTestBase.cs
```

### Test Naming Convention
Use the pattern: `MethodName_Scenario_ExpectedBehavior`

```csharp
[Fact]
public async Task ConnectToDeviceAsync_ValidDevice_ReturnsTrue()
{
    // Arrange
    var mockService = new Mock<IBluetoothService>();
    var device = new MockGloveDevice();

    // Act
    var result = await mockService.Object.ConnectToDeviceAsync(device);

    // Assert
    Assert.True(result);
}

[Fact]
public async Task ConnectToDeviceAsync_DeviceNotFound_ThrowsException()
{
    // ...
}
```

### Bluetooth Mocking
Create mock implementations for testing without hardware:

```csharp
public class MockBluetoothService : IBluetoothService
{
    public bool IsConnected { get; set; }
    public List<IDevice> DiscoveredDevices { get; set; } = new();

    public Task<bool> ConnectToDeviceAsync(IDevice device)
    {
        IsConnected = true;
        return Task.FromResult(true);
    }

    // ... other interface implementations
}
```

### ViewModel Testing
Test ViewModels without UI dependencies:

```csharp
public class MainPageViewModelTests
{
    [Fact]
    public async Task ScanForDevices_FindsDevices_UpdatesDevicesList()
    {
        // Arrange
        var mockBluetooth = new Mock<IBluetoothService>();
        mockBluetooth.Setup(x => x.ScanForDevicesAsync(It.IsAny<TimeSpan>()))
            .ReturnsAsync(new List<IDevice> { new MockGloveDevice() });

        var viewModel = new MainPageViewModel(mockBluetooth.Object);

        // Act
        await viewModel.ScanForDevicesCommand.ExecuteAsync(null);

        // Assert
        Assert.Single(viewModel.AvailableDevices);
    }
}
```

---

## MAUI Best Practices

### XAML vs C# UI
- **Prefer XAML** for static UI layouts and data binding
- **Use C#** for dynamic UI generation, complex animations, or platform-specific rendering

### Resource Dictionaries
Organize styles in `Resources/Styles/`. All color and style values come from the
**design system** (`docs/design/`) — the authoritative visual spec derived from
bluebuzzah.com (dark theme only; blue `#35B6F2` = success, never green; dark text on blue fills):

```xaml
<!-- Colors.xaml (see docs/design/colors.md for the full token set) -->
<Color x:Key="Primary">#35B6F2</Color>
<Color x:Key="PageBackground">#0a0a0a</Color>
<Color x:Key="CardBackground">#05212D</Color>

<!-- Styles.xaml (per-control specs in docs/design/components.md) -->
<Style x:Key="LargeButton" TargetType="Button">
    <Setter Property="HeightRequest" Value="56" />
    <Setter Property="FontSize" Value="18" />
    <Setter Property="CornerRadius" Value="8" />
</Style>
```

### Platform-Specific Code
Use conditional compilation for platform-specific code:

```csharp
#if ANDROID
using Android.App;
#elif IOS
using UIKit;
#endif

public void ConfigurePlatformFeatures()
{
#if ANDROID
    // Android-specific code
#elif IOS
    // iOS-specific code
#endif
}
```

Or use platform abstractions in `/Platforms/`:
```
Platforms/
├── Android/
│   └── BluetoothPermissionService.cs
└── iOS/
    └── BluetoothPermissionService.cs
```

### Shell Navigation
Use Shell for app navigation:

```xaml
<Shell>
    <TabBar>
        <ShellContent Title="Home" Icon="home.png" ContentTemplate="{DataTemplate views:MainPage}" />
        <ShellContent Title="Control" Icon="control.png" ContentTemplate="{DataTemplate views:GloveControlPage}" />
    </TabBar>
</Shell>
```

Navigate in code:
```csharp
await Shell.Current.GoToAsync("//control");
await Shell.Current.GoToAsync($"details?id={deviceId}");
```

### Data Binding
Use XAML binding for reactive UI:

```xaml
<Label Text="{Binding DeviceName}" />
<Button Text="Connect"
        Command="{Binding ConnectCommand}"
        IsEnabled="{Binding CanConnect}" />
```

ViewModel implementation:
```csharp
public class GloveControlViewModel : INotifyPropertyChanged
{
    private string _deviceName = string.Empty;
    public string DeviceName
    {
        get => _deviceName;
        set
        {
            _deviceName = value;
            OnPropertyChanged(nameof(DeviceName));
        }
    }

    public ICommand ConnectCommand { get; }

    // INotifyPropertyChanged implementation
}
```

### Hot Reload
Use XAML Hot Reload for rapid UI iteration:
- Modify XAML → Changes appear instantly in emulator/device
- Works with data bindings, styles, layouts
- C# changes require rebuild

---

## Development Workflow

### Build Commands

**iOS Simulator:**
```bash
dotnet build -f net9.0-ios -t:Run
```

**Android Emulator:**
```bash
dotnet build -f net9.0-android -t:Run
```

**MacCatalyst:**
```bash
dotnet build -f net9.0-maccatalyst -t:Run
```

### Debugging
- Use Visual Studio 2022 (Windows/Mac) or VS Code with C# DevKit
- Set breakpoints in C# code and XAML code-behind
- Use **Live Visual Tree** to inspect XAML hierarchy
- Enable **XAML Hot Reload** in preferences

### Testing
```bash
dotnet test
```

### Package Management
```bash
dotnet add package Plugin.BLE
dotnet restore
```

---

## Best Practices Summary

✅ **DO:**
- Use MVVM pattern with clear separation of concerns
- Implement comprehensive accessibility (SemanticProperties, high contrast, large touch targets)
- Mock Bluetooth services for testing
- Follow .NET naming conventions
- Document public APIs with XML comments
- Use async/await for all I/O operations
- Enable nullable reference types
- Test ViewModels independently from Views

❌ **DON'T:**
- Put business logic in code-behind or Views
- Use hardcoded strings (use resources/localization)
- Ignore accessibility requirements
- Use `async void` (except event handlers)
- Forget error handling for Bluetooth operations
- Create small touch targets (<44pt)
- Skip semantic properties on interactive elements

---

## Additional Resources

### .NET MAUI Documentation
- [Official MAUI Docs](https://learn.microsoft.com/en-us/dotnet/maui/)
- [MAUI Community Toolkit](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/maui/)

### Bluetooth
- [Plugin.BLE Documentation](https://github.com/dotnet-bluetooth-le/dotnet-bluetooth-le)
- [BLE Overview](https://learn.microsoft.com/en-us/xamarin/android/data-cloud/bluetooth)

### Accessibility
- [WCAG 2.1 Guidelines](https://www.w3.org/WAI/WCAG21/quickref/)
- [iOS Accessibility (Apple HIG)](https://developer.apple.com/design/human-interface-guidelines/accessibility)
- [Android Accessibility](https://developer.android.com/guide/topics/ui/accessibility)

### Project Design Docs
- `docs/design/` — **Design system** (visual source of truth: colors, typography, components, motion)
- `DESIGN_GUIDE.md` — UX and interaction patterns

### Parkinson's-Specific Design
- [Designing for Motor Impairments](https://www.w3.org/WAI/perspective-videos/controls/)
- [Tremor-Friendly UI Patterns](https://webaim.org/articles/motor/)

---

## Contact & Support

For questions or contributions related to BuzzahBuddy development, refer to the BlueBuzzah community resources and project repository.

**Version:** 1.1
**Last Updated:** 2026-07-14
