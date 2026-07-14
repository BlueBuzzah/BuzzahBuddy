# Design System Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring every XAML resource, style, view, and code-behind color in BuzzahBuddy into compliance with the design system at `docs/design/` (colors, typography, components, motion).

**Architecture:** All visual tokens live in `BuzzahBuddy/Resources/Styles/Colors.xaml`; all control styles in `BuzzahBuddy/Resources/Styles/Styles.xaml`. Views reference tokens/styles only — the sweep fixes tokens first, then styles, then the handful of view-level overrides and code-behind color lookups. No new dependencies.

**Tech Stack:** .NET MAUI (net9.0, iOS/Android/MacCatalyst), XAML resource dictionaries.

## Global Constraints

- **Dark theme only.** Never introduce `AppThemeBinding` light values.
- **Blue = success/info/progress: `#35B6F2`. Never green/teal.**
- **Never light text on the blue fill** — text on `#35B6F2` is always `#0a0a0a` (`PrimaryDarkText`).
- **Danger as text on dark = `#fb7185`** (`DangerDark`); `#e11d48`/`#dc2626` are fills only, with `#fafafa` text.
- **System fonts only** — no `FontFamily` anywhere, no bundled font assets.
- **Border token is `#0A3143`** (one step darker than the `#0d3a4d` elevation-2 surface).
- Radii: **8** buttons/inputs, **12** cards. Touch targets ≥ 44pt. Glows are `#35B6F2` alpha shadows, never gray/black.
- Verification for XAML work is `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-maccatalyst` (styles are not unit-testable); expect `Build succeeded`.
- Commit after every task. **No AI attribution in commit messages — ever.**

---

### Task 1: Colors.xaml token remediation + its two consumers

Fix the token file per `docs/design/colors.md` "Current implementation gaps", add the spec's translucent chip tokens, and update the only two consumers of the deleted tokens (ConnectionStatusBanner code-behind and the Stop Session button).

**Files:**
- Modify: `BuzzahBuddy/Resources/Styles/Colors.xaml`
- Modify: `BuzzahBuddy/Views/Controls/ConnectionStatusBanner.xaml.cs:138-237`
- Modify: `BuzzahBuddy/Views/GloveControlPage.xaml:416` (Stop Session button)

**Interfaces:**
- Produces resource keys later tasks use: `PrimaryChipBackground`, `WarningChipBackground`, `DangerChipBackground`; changed values for `Success` (`#35B6F2`) and `CardBorder` (`#0A3143`).
- Deletes keys: `SuccessDark`, `SuccessLight`, `SuccessLightDark`, `Warning{Dark,Light,LightDark}`, `Danger{Light,LightDark}`, `Info`, `InfoDark`, `InfoLight`, `InfoLightDark`, `InfoBrush`, all six `Pastel*` keys. (`DangerDark` **stays** — it is the spec's `#fb7185` danger-text token.)

- [ ] **Step 1: Edit `Colors.xaml`**

Replace the status-color block (lines 48-78) with:

```xml
    <!-- Semantic status colors (docs/design/colors.md) -->
    <Color x:Key="Success">#35B6F2</Color>
    <Color x:Key="Warning">#f59e0b</Color>
    <Color x:Key="Danger">#e11d48</Color>
    <Color x:Key="DangerDark">#fb7185</Color>

    <!-- Translucent chip backgrounds: accent @ 10% alpha over dark base -->
    <Color x:Key="PrimaryChipBackground">#1A35B6F2</Color>
    <Color x:Key="WarningChipBackground">#1AF59E0B</Color>
    <Color x:Key="DangerChipBackground">#1AE11D48</Color>

    <!-- Saturated Button Colors (WCAG AA Compliant) -->
    <Color x:Key="DangerButton">#dc2626</Color>
    <Color x:Key="DangerButtonText">#fafafa</Color>
    <Color x:Key="WarningButton">#f59e0b</Color>
    <Color x:Key="WarningButtonText">#0a0a0a</Color>
```

Also in the same file:
- Change `CardBorder` from `#0d3a4d` to `#0A3143` (line 31).
- Delete the `InfoBrush` line (line 107).
- Leave the Gray ramp alone for now (Task 5 retires it).

Note `WarningButton` changes `#d97706 → #f59e0b` to match the spec's single warning fill; its dark text is 9.2:1 on `#f59e0b`.

- [ ] **Step 2: Update `ConnectionStatusBanner.xaml.cs` color mappings**

In `GetBannerBackgroundColor()` (line 138):

```csharp
    private Color GetBannerBackgroundColor()
    {
        if (!IsBluetoothEnabled)
        {
            return GetColorResource("WarningChipBackground");
        }

        return ConnectionState switch
        {
            ConnectionState.Connected => GetColorResource("PrimaryChipBackground"),
            ConnectionState.Connecting => GetColorResource("PrimaryChipBackground"),
            ConnectionState.Disconnected => GetColorResource("CardBackgroundLight"),
            ConnectionState.Error => GetColorResource("DangerChipBackground"),
            _ => GetColorResource("CardBackgroundLight")
        };
    }
```

In `GetStatusTextColor()` (line 206):

```csharp
    private Color GetStatusTextColor()
    {
        if (!IsBluetoothEnabled)
        {
            return GetColorResource("Warning");
        }

        return ConnectionState switch
        {
            ConnectionState.Connected => GetColorResource("Primary"),
            ConnectionState.Connecting => GetColorResource("Primary"),
            ConnectionState.Disconnected => GetColorResource("TextMuted"),
            ConnectionState.Error => GetColorResource("DangerDark"),
            _ => GetColorResource("TextMuted")
        };
    }
```

In `GetDetailTextColor()` (line 233): replace `GetColorResource("Gray400")` with `GetColorResource("TextMuted")`.

- [ ] **Step 3: Fix the Stop Session button (GloveControlPage.xaml:416)**

It is a destructive action wearing the deleted `PastelDanger`. Replace:

```xml
                        BackgroundColor="{StaticResource PastelDanger}"
```

with:

```xml
                        BackgroundColor="{StaticResource DangerButton}"
                        TextColor="{StaticResource DangerButtonText}"
```

- [ ] **Step 4: Verify no dangling references, then build**

Run: `grep -rnE 'StaticResource (Pastel|Info[A-Z"]|SuccessDark|SuccessLight|WarningDark|WarningLight|DangerLight)' BuzzahBuddy --include='*.xaml' --include='*.cs' | grep -v obj/`
Expected: no output.

Run: `grep -rn '"\(SuccessLightDark\|InfoLightDark\|SuccessDark\|InfoDark\|WarningLightDark\|DangerLightDark\|WarningDark\|Gray400\)"' BuzzahBuddy --include='*.cs' | grep -v obj/`
Expected: no output.

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-maccatalyst`
Expected: `Build succeeded`

- [ ] **Step 5: Commit**

```bash
git add BuzzahBuddy/Resources/Styles/Colors.xaml BuzzahBuddy/Views/Controls/ConnectionStatusBanner.xaml.cs BuzzahBuddy/Views/GloveControlPage.xaml
git commit -m "fix: align color tokens with design system (blue success, spec border, chip tokens)"
```

---

### Task 2: Button styles per components.md

The implicit `Button` style currently pairs `#fafafa` text with the `#35B6F2` fill — the spec's #1 forbidden pair. Keyed button styles in MAUI **replace** (not extend) the implicit style, so each keyed style must be self-contained. Keep the DESIGN_GUIDE Wave-2 large sizes (tremor-friendly) but use `MinimumHeightRequest` so text scaling grows instead of clips.

**Files:**
- Modify: `BuzzahBuddy/Resources/Styles/Styles.xaml:28-93, 516-537`
- Modify: `BuzzahBuddy/Views/MainPage.xaml:34` area, `BuzzahBuddy/Views/GloveControlPage.xaml:19` area, `BuzzahBuddy/Views/SettingsPage.xaml:52-53, 155-156`, `BuzzahBuddy/Views/DeviceListPage.xaml:45-46`, `BuzzahBuddy/Views/CalibrationPage.xaml:383`

**Interfaces:**
- Produces keyed styles views use: `PrimaryButton`, `SecondaryButton` (outline), `TertiaryButton` (ghost), `DangerActionButton`, `WarningActionButton`, plus existing `PrimaryButtonGlow`, `SecondaryButtonGlow`, `FingerTestButton`.

- [ ] **Step 1: Replace the implicit `Button` style (Styles.xaml lines 28-52)**

```xml
    <Style TargetType="Button">
        <Setter Property="TextColor" Value="{StaticResource PrimaryDarkText}" />
        <Setter Property="BackgroundColor" Value="{StaticResource Primary}" />
        <Setter Property="FontSize" Value="16"/>
        <Setter Property="FontAttributes" Value="Bold"/>
        <Setter Property="BorderWidth" Value="0"/>
        <Setter Property="CornerRadius" Value="8"/>
        <Setter Property="Padding" Value="24,12"/>
        <Setter Property="MinimumHeightRequest" Value="48"/>
        <Setter Property="MinimumWidthRequest" Value="48"/>
        <Setter Property="VisualStateManager.VisualStateGroups">
            <VisualStateGroupList>
                <VisualStateGroup x:Name="CommonStates">
                    <VisualState x:Name="Normal" />
                    <VisualState x:Name="Pressed">
                        <VisualState.Setters>
                            <Setter Property="BackgroundColor" Value="{StaticResource PrimaryHover}" />
                        </VisualState.Setters>
                    </VisualState>
                    <VisualState x:Name="Disabled">
                        <VisualState.Setters>
                            <Setter Property="TextColor" Value="{StaticResource TextMuted}" />
                            <Setter Property="BackgroundColor" Value="{StaticResource CardBackgroundLight}" />
                        </VisualState.Setters>
                    </VisualState>
                    <VisualState x:Name="PointerOver" />
                </VisualStateGroup>
            </VisualStateGroupList>
        </Setter>
    </Style>
```

- [ ] **Step 2: Replace the keyed button styles (Styles.xaml lines 54-93)**

```xml
    <!-- Visual Hierarchy: Standardized Button Styles (DESIGN_GUIDE.md Wave 2 sizes, docs/design/components.md colors) -->

    <!-- Primary Button: the one blue CTA per screen -->
    <Style x:Key="PrimaryButton" TargetType="Button">
        <Setter Property="TextColor" Value="{StaticResource PrimaryDarkText}" />
        <Setter Property="BackgroundColor" Value="{StaticResource Primary}" />
        <Setter Property="MinimumHeightRequest" Value="72" />
        <Setter Property="FontSize" Value="20" />
        <Setter Property="FontAttributes" Value="Bold" />
        <Setter Property="Padding" Value="24,20" />
        <Setter Property="CornerRadius" Value="8" />
        <Setter Property="VisualStateManager.VisualStateGroups">
            <VisualStateGroupList>
                <VisualStateGroup x:Name="CommonStates">
                    <VisualState x:Name="Normal" />
                    <VisualState x:Name="Pressed">
                        <VisualState.Setters>
                            <Setter Property="BackgroundColor" Value="{StaticResource PrimaryHover}" />
                        </VisualState.Setters>
                    </VisualState>
                    <VisualState x:Name="Disabled">
                        <VisualState.Setters>
                            <Setter Property="TextColor" Value="{StaticResource TextMuted}" />
                            <Setter Property="BackgroundColor" Value="{StaticResource CardBackgroundLight}" />
                        </VisualState.Setters>
                    </VisualState>
                </VisualStateGroup>
            </VisualStateGroupList>
        </Setter>
    </Style>

    <!-- Secondary Button: outline -->
    <Style x:Key="SecondaryButton" TargetType="Button">
        <Setter Property="TextColor" Value="{StaticResource TextPrimary}" />
        <Setter Property="BackgroundColor" Value="Transparent" />
        <Setter Property="BorderColor" Value="{StaticResource CardBorder}" />
        <Setter Property="BorderWidth" Value="1" />
        <Setter Property="MinimumHeightRequest" Value="56" />
        <Setter Property="FontSize" Value="18" />
        <Setter Property="FontAttributes" Value="Bold" />
        <Setter Property="Padding" Value="20,16" />
        <Setter Property="CornerRadius" Value="8" />
        <Setter Property="VisualStateManager.VisualStateGroups">
            <VisualStateGroupList>
                <VisualStateGroup x:Name="CommonStates">
                    <VisualState x:Name="Normal" />
                    <VisualState x:Name="Pressed">
                        <VisualState.Setters>
                            <Setter Property="BackgroundColor" Value="{StaticResource CardBackground}" />
                        </VisualState.Setters>
                    </VisualState>
                    <VisualState x:Name="Disabled">
                        <VisualState.Setters>
                            <Setter Property="TextColor" Value="{StaticResource TextMuted}" />
                        </VisualState.Setters>
                    </VisualState>
                </VisualStateGroup>
            </VisualStateGroupList>
        </Setter>
    </Style>

    <!-- Tertiary Button: text/ghost -->
    <Style x:Key="TertiaryButton" TargetType="Button">
        <Setter Property="TextColor" Value="{StaticResource Primary}" />
        <Setter Property="BackgroundColor" Value="Transparent" />
        <Setter Property="MinimumHeightRequest" Value="48" />
        <Setter Property="FontSize" Value="16" />
        <Setter Property="FontAttributes" Value="Bold" />
        <Setter Property="Padding" Value="16,12" />
        <Setter Property="CornerRadius" Value="8" />
        <Setter Property="VisualStateManager.VisualStateGroups">
            <VisualStateGroupList>
                <VisualStateGroup x:Name="CommonStates">
                    <VisualState x:Name="Normal" />
                    <VisualState x:Name="Pressed">
                        <VisualState.Setters>
                            <Setter Property="BackgroundColor" Value="{StaticResource PrimaryChipBackground}" />
                        </VisualState.Setters>
                    </VisualState>
                    <VisualState x:Name="Disabled">
                        <VisualState.Setters>
                            <Setter Property="TextColor" Value="{StaticResource TextMuted}" />
                        </VisualState.Setters>
                    </VisualState>
                </VisualStateGroup>
            </VisualStateGroupList>
        </Setter>
    </Style>

    <!-- Danger Button: destructive actions only, always behind a confirmation -->
    <Style x:Key="DangerActionButton" TargetType="Button">
        <Setter Property="TextColor" Value="{StaticResource DangerButtonText}" />
        <Setter Property="BackgroundColor" Value="{StaticResource DangerButton}" />
        <Setter Property="MinimumHeightRequest" Value="56" />
        <Setter Property="FontSize" Value="18" />
        <Setter Property="FontAttributes" Value="Bold" />
        <Setter Property="Padding" Value="20,16" />
        <Setter Property="CornerRadius" Value="8" />
        <Setter Property="VisualStateManager.VisualStateGroups">
            <VisualStateGroupList>
                <VisualStateGroup x:Name="CommonStates">
                    <VisualState x:Name="Normal" />
                    <VisualState x:Name="Pressed">
                        <VisualState.Setters>
                            <Setter Property="BackgroundColor" Value="#b91c1c" />
                        </VisualState.Setters>
                    </VisualState>
                    <VisualState x:Name="Disabled">
                        <VisualState.Setters>
                            <Setter Property="TextColor" Value="{StaticResource TextMuted}" />
                            <Setter Property="BackgroundColor" Value="{StaticResource CardBackgroundLight}" />
                        </VisualState.Setters>
                    </VisualState>
                </VisualStateGroup>
            </VisualStateGroupList>
        </Setter>
    </Style>

    <!-- Warning Button: caution actions (dark text on amber per contrast matrix) -->
    <Style x:Key="WarningActionButton" TargetType="Button">
        <Setter Property="TextColor" Value="{StaticResource WarningButtonText}" />
        <Setter Property="BackgroundColor" Value="{StaticResource WarningButton}" />
        <Setter Property="MinimumHeightRequest" Value="56" />
        <Setter Property="FontSize" Value="18" />
        <Setter Property="FontAttributes" Value="Bold" />
        <Setter Property="Padding" Value="20,16" />
        <Setter Property="CornerRadius" Value="8" />
        <Setter Property="VisualStateManager.VisualStateGroups">
            <VisualStateGroupList>
                <VisualStateGroup x:Name="CommonStates">
                    <VisualState x:Name="Normal" />
                    <VisualState x:Name="Disabled">
                        <VisualState.Setters>
                            <Setter Property="TextColor" Value="{StaticResource TextMuted}" />
                            <Setter Property="BackgroundColor" Value="{StaticResource CardBackgroundLight}" />
                        </VisualState.Setters>
                    </VisualState>
                </VisualStateGroup>
            </VisualStateGroupList>
        </Setter>
    </Style>
```

Also in `FingerTestButton` (line 85): add `<Setter Property="BackgroundColor" Value="{StaticResource Primary}" />` and change `CornerRadius` to `8` (keyed styles don't inherit the implicit style's fill).

- [ ] **Step 3: Retarget view-level button overrides**

Ad-hoc `BackgroundColor`/`TextColor` combos now have proper styles. In each file, replace the style + color-override pair with the single keyed style (drop the `BackgroundColor` and `TextColor` attribute lines):

- `Views/DeviceListPage.xaml:44-46` — `Style="{StaticResource SecondaryButton}"` + `DangerButton`/`DangerButtonText` colors → `Style="{StaticResource DangerActionButton}"`.
- `Views/SettingsPage.xaml:51-53` — same pattern → `Style="{StaticResource DangerActionButton}"`.
- `Views/SettingsPage.xaml:154-156` — style + `WarningButton`/`WarningButtonText` → `Style="{StaticResource WarningActionButton}"`.
- `Views/MainPage.xaml:33-35` — button with `BackgroundColor="{StaticResource Warning}"` → `Style="{StaticResource WarningActionButton}"`, drop the `BackgroundColor` line.
- `Views/GloveControlPage.xaml:18-20` — same pattern → `Style="{StaticResource WarningActionButton}"`, drop the `BackgroundColor` line.
- `Views/GloveControlPage.xaml:414-416` — Stop Session (now `DangerButton`/`DangerButtonText` from Task 1) → `Style="{StaticResource DangerActionButton}"`, drop both color lines.
- `Views/CalibrationPage.xaml:383` — delete the `BackgroundColor="{StaticResource Gray700}"` line (TertiaryButton is now a proper ghost).

- [ ] **Step 4: Verify and build**

Run: `grep -rn 'StaticResource \(DangerButton\|WarningButton\|Warning\)}' BuzzahBuddy/Views --include='*.xaml'`
Expected: no button `BackgroundColor` overrides remain (label `TextColor="{StaticResource Warning}"` usages are fine and expected).

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-maccatalyst`
Expected: `Build succeeded`

- [ ] **Step 5: Commit**

```bash
git add BuzzahBuddy/Resources/Styles/Styles.xaml BuzzahBuddy/Views
git commit -m "fix: button styles per design system (dark-on-blue text, outline/ghost variants, danger/warning styles)"
```

---

### Task 3: Typography — system fonts + spec scale

**Files:**
- Modify: `BuzzahBuddy/Resources/Styles/Styles.xaml` (10 `FontFamily` setters; heading styles at lines 220-267)
- Modify: `BuzzahBuddy/MauiProgram.cs` (`ConfigureFonts` block)
- Delete: `BuzzahBuddy/Resources/Fonts/OpenSans-Regular.ttf`, `BuzzahBuddy/Resources/Fonts/OpenSans-Semibold.ttf`

**Interfaces:**
- Consumes nothing new. Label style keys keep their names; only sizes/colors change.

- [ ] **Step 1: Remove every `FontFamily` setter from Styles.xaml**

Delete all 10 lines matching `<Setter Property="FontFamily"` (Button, DatePicker, Editor, Entry, Label, Picker, RadioButton, SearchBar, SearchHandler, TimePicker). System fonts apply automatically when `FontFamily` is unset.

- [ ] **Step 2: Update the heading/label scale (Styles.xaml lines 220-267)**

Per `docs/design/typography.md` (MAUI has no Semibold — Bold is the approved stand-in; size and color carry the rest):

```xml
    <!-- Typography scale (docs/design/typography.md) -->
    <Style TargetType="Label" x:Key="Headline">
        <Setter Property="TextColor" Value="{StaticResource TextPrimary}" />
        <Setter Property="FontSize" Value="32" />
        <Setter Property="FontAttributes" Value="Bold" />
        <Setter Property="HorizontalOptions" Value="Center" />
        <Setter Property="HorizontalTextAlignment" Value="Center" />
    </Style>

    <Style TargetType="Label" x:Key="Heading1">
        <Setter Property="TextColor" Value="{StaticResource TextPrimary}" />
        <Setter Property="FontSize" Value="24" />
        <Setter Property="FontAttributes" Value="Bold" />
    </Style>

    <Style TargetType="Label" x:Key="Heading2">
        <Setter Property="TextColor" Value="{StaticResource TextPrimary}" />
        <Setter Property="FontSize" Value="20" />
        <Setter Property="FontAttributes" Value="Bold" />
    </Style>

    <Style TargetType="Label" x:Key="Heading3">
        <Setter Property="TextColor" Value="{StaticResource TextPrimary}" />
        <Setter Property="FontSize" Value="17" />
        <Setter Property="FontAttributes" Value="Bold" />
    </Style>

    <Style TargetType="Label" x:Key="SubHeadline">
        <Setter Property="TextColor" Value="{StaticResource TextMuted}" />
        <Setter Property="FontSize" Value="18" />
        <Setter Property="HorizontalOptions" Value="Center" />
        <Setter Property="HorizontalTextAlignment" Value="Center" />
    </Style>

    <Style TargetType="Label" x:Key="BodyText">
        <Setter Property="FontSize" Value="16" />
        <Setter Property="LineHeight" Value="1.5" />
    </Style>

    <Style TargetType="Label" x:Key="SecondaryText">
        <Setter Property="FontSize" Value="14" />
        <Setter Property="TextColor" Value="{StaticResource TextMuted}" />
    </Style>

    <Style TargetType="Label" x:Key="CaptionText">
        <Setter Property="FontSize" Value="12" />
        <Setter Property="TextColor" Value="{StaticResource TextMuted}" />
    </Style>
```

Sanity-check the single `SubHeadline` consumer (`Views/MainPage.xaml:26`): it must be a supporting line under a headline (muted 18pt Regular is correct there). If it is being used as a section header instead, restyle that one label to `Heading2`.

- [ ] **Step 3: Remove font registration and assets**

In `MauiProgram.cs`, change:

```csharp
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});
```

to:

```csharp
		builder
			.UseMauiApp<App>();
```

Then: `git rm BuzzahBuddy/Resources/Fonts/OpenSans-Regular.ttf BuzzahBuddy/Resources/Fonts/OpenSans-Semibold.ttf`

The csproj uses a wildcard (`<MauiFont Include="Resources\Fonts\*" />` at line 71) — leave it; an empty folder is fine.

- [ ] **Step 4: Verify and build**

Run: `grep -rn "OpenSans\|FontFamily" BuzzahBuddy --include='*.xaml' --include='*.cs' | grep -v obj/`
Expected: no output.

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-maccatalyst`
Expected: `Build succeeded`

- [ ] **Step 5: Commit**

```bash
git add -A BuzzahBuddy/Resources BuzzahBuddy/MauiProgram.cs
git commit -m "feat: system fonts and spec typography scale, drop OpenSans assets"
```

---

### Task 4: Input fields — bordered Entry/Picker pattern with focus ring

MAUI `Entry` has no native border; `docs/design/components.md` requires card-background fields with a 1pt `#0A3143` border that turns `#35B6F2` on focus. All 9 inputs live in `ProfileSettingsPage.xaml`.

**Files:**
- Create: `BuzzahBuddy/Behaviors/InputFocusBehavior.cs`
- Modify: `BuzzahBuddy/Resources/Styles/Styles.xaml` (Entry/Editor/Picker placeholder colors + new `InputBorder` style)
- Modify: `BuzzahBuddy/Views/ProfileSettingsPage.xaml:55-131` (7 Entries + 2 Pickers)

**Interfaces:**
- Produces: `InputBorder` Border style; `BuzzahBuddy.Behaviors.InputFocusBehavior` (attach to `Border` wrapping a focusable input).

- [ ] **Step 1: Add the `InputBorder` style to Styles.xaml** (near the Border style, after line 22)

```xml
    <!-- Input field wrapper: Entry/Picker have no native border (docs/design/components.md) -->
    <Style x:Key="InputBorder" TargetType="Border">
        <Setter Property="BackgroundColor" Value="{StaticResource CardBackground}" />
        <Setter Property="Stroke" Value="{StaticResource CardBorder}" />
        <Setter Property="StrokeThickness" Value="1" />
        <Setter Property="StrokeShape" Value="RoundRectangle 8" />
        <Setter Property="Padding" Value="8,0" />
        <Setter Property="MinimumHeightRequest" Value="44" />
    </Style>
```

- [ ] **Step 2: Fix placeholder/title colors in Styles.xaml**

Replace every `PlaceholderColor`/`CancelButtonColor` value of `Gray500` with `TextMuted` (Editor, Entry, SearchBar, SearchHandler). Replace the Picker `TitleColor` value `Gray200` (currently near-invisible dark-on-dark) with `TextMuted`.

- [ ] **Step 3: Write the focus behavior**

Create `BuzzahBuddy/Behaviors/InputFocusBehavior.cs`:

```csharp
namespace BuzzahBuddy.Behaviors;

/// <summary>
/// Attached to a Border wrapping a focusable input: turns the stroke into the
/// primary focus ring while the inner input has focus (docs/design/components.md).
/// </summary>
public class InputFocusBehavior : Behavior<Border>
{
    protected override void OnAttachedTo(Border border)
    {
        base.OnAttachedTo(border);
        if (border.Content is VisualElement input)
        {
            input.Focused += (_, _) => SetStroke(border, "Primary");
            input.Unfocused += (_, _) => SetStroke(border, "CardBorder");
        }
    }

    private static void SetStroke(Border border, string colorKey)
    {
        if (Application.Current?.Resources.TryGetValue(colorKey, out var value) == true && value is Color color)
            border.Stroke = new SolidColorBrush(color);
    }
}
```

(Handlers are not detached in `OnDetachingFrom` — the behavior lives and dies with its Border; no leak path. `// ponytail:` comment optional.)

- [ ] **Step 4: Wrap the 9 inputs in ProfileSettingsPage.xaml**

Add the namespace to the page root: `xmlns:behaviors="clr-namespace:BuzzahBuddy.Behaviors"`.

For each `Entry`/`Picker` (lines 55-131), move the `Grid.Row`/`Grid.Column` attributes to a wrapping Border and keep everything else on the input. Pattern (FrequencyEntry shown; repeat for all 9):

```xml
                        <Border Grid.Row="1" Grid.Column="1" Style="{StaticResource InputBorder}">
                            <Border.Behaviors>
                                <behaviors:InputFocusBehavior />
                            </Border.Behaviors>
                            <Entry Text="{Binding FrequencyText}"
                                   Keyboard="Numeric"
                                   MinimumHeightRequest="48"
                                   HorizontalTextAlignment="End"
                                   SemanticProperties.Description="Actuator frequency in hertz, 50 to 300"
                                   AutomationId="FrequencyEntry" />
                        </Border>
```

- [ ] **Step 5: Verify and build**

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-maccatalyst`
Expected: `Build succeeded`

Run: `grep -c "InputBorder" BuzzahBuddy/Views/ProfileSettingsPage.xaml`
Expected: `9`

- [ ] **Step 6: Commit**

```bash
git add BuzzahBuddy/Behaviors BuzzahBuddy/Resources/Styles/Styles.xaml BuzzahBuddy/Views/ProfileSettingsPage.xaml
git commit -m "feat: bordered input fields with focus ring per design system"
```

---

### Task 5: Retire the Gray ramp — named tokens everywhere

The blue-tinted `Gray100-950` ramp has no website counterpart. Every remaining use maps 1:1 to a named token (`Gray600` = `#0d3a4d` = `CardBackgroundLight`, `Gray400` = `#a3a3a3` = `TextMuted`, etc.), so this is a rename sweep, then deletion.

**Files:**
- Modify: `BuzzahBuddy/Resources/Styles/Styles.xaml` (all `Gray*` references)
- Modify: `BuzzahBuddy/Views/GloveControlPage.xaml:287` (`Gray950` card)
- Modify: `BuzzahBuddy/Resources/Styles/Colors.xaml` (delete ramp + gray brushes)

**Interfaces:**
- Deletes keys: `Gray100`-`Gray950` colors and `Gray100Brush`-`Gray950Brush`.

- [ ] **Step 1: Replace Gray references in Styles.xaml**

| Old | New | Where |
|---|---|---|
| `Gray600` | `CardBackgroundLight` | all Disabled visual states (CheckBox, Editor, Entry, Label, Picker, ProgressBar, RadioButton, SearchBar, SearchHandler, Slider, Switch, TimePicker) and Slider `MaximumTrackColor` |
| `Gray500` | `TextMuted` | DatePicker disabled text, Switch Off thumb |
| `Gray400` | `TextMuted` | (base Button was fixed in Task 2 — verify none remain) |
| `Gray200` | `CardBorder` | BoxView background |
| `Gray100` | `TextPrimary` | IndicatorView `SelectedIndicatorColor` |
| `Gray500` (IndicatorView `IndicatorColor`) | `CardBackgroundLight` | |
| `Gray950` | `PageBackground` | Shell `DisabledColor`, TabbedPage `UnselectedTabColor` |

Note: disabled *text* going from `Gray600` (`#0d3a4d`, near-invisible) to nothing readable is intentional-looking but wrong — for text-color Disabled setters specifically (Editor, Entry, Label, Picker, RadioButton, SearchBar, SearchHandler, TimePicker), use `TextMuted` (spec: inactive = `#a3a3a3`), and keep `CardBackgroundLight` only for background/track Disabled setters (ProgressBar, Slider, Switch, CheckBox color).

- [ ] **Step 2: Fix the Gray950 card (GloveControlPage.xaml:287)**

The "Profile Details" Border uses `Gray950` (page black) as a card background. Cards are `#05212D`: change `BackgroundColor="{StaticResource Gray950}"` to `BackgroundColor="{StaticResource CardBackground}"`.

- [ ] **Step 3: Delete the ramp from Colors.xaml**

Remove the `Gray100`-`Gray950` `<Color>` block (lines 37-46) and the `Gray100Brush`-`Gray950Brush` block (lines 94-102).

- [ ] **Step 4: Verify and build**

Run: `grep -rn "Gray[0-9]" BuzzahBuddy --include='*.xaml' --include='*.cs' | grep -v obj/`
Expected: no output.

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-maccatalyst`
Expected: `Build succeeded`

- [ ] **Step 5: Commit**

```bash
git add BuzzahBuddy/Resources/Styles BuzzahBuddy/Views/GloveControlPage.xaml
git commit -m "refactor: replace gray ramp with named design-system tokens"
```

---

### Task 6: Remaining control specs + motion (reduced-motion support)

**Files:**
- Modify: `BuzzahBuddy/Resources/Styles/Styles.xaml` (Switch)
- Create: `BuzzahBuddy/Helpers/Motion.cs`
- Modify: `BuzzahBuddy/Views/GloveControlPage.xaml.cs:21-22`

**Interfaces:**
- Produces: `BuzzahBuddy.Helpers.Motion.Reduce` (static bool; true when the OS requests reduced motion).

- [ ] **Step 1: Switch per components.md** (On: blue track + dark thumb; Off: `#0d3a4d` track + muted thumb)

In the `Switch` style, change the default/On `ThumbColor` from `White` to `Black` (i.e. `#0a0a0a`) — both the top-level setter and the `On` visual state. Leave the Off state thumb as `TextMuted` (done in Task 5) and Disabled as-is. MAUI has no cross-platform Off-track color property; the platform default dark track is acceptable.

- [ ] **Step 2: Create the reduced-motion helper**

`BuzzahBuddy/Helpers/Motion.cs`:

```csharp
namespace BuzzahBuddy.Helpers;

/// <summary>OS-level reduce-motion preference (docs/design/motion.md: mandatory).</summary>
public static class Motion
{
    public static bool Reduce
    {
        get
        {
#if IOS || MACCATALYST
            return UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif ANDROID
            try
            {
                var resolver = Android.App.Application.Context.ContentResolver;
                return Android.Provider.Settings.Global.GetFloat(
                    resolver, Android.Provider.Settings.Global.AnimatorDurationScale, 1f) == 0f;
            }
            catch
            {
                return false;
            }
#else
            return false;
#endif
        }
    }
}
```

- [ ] **Step 3: Bring the one animation to spec**

`GloveControlPage.xaml.cs:21-22` currently scales to 0.97 over 70ms. Spec: press scale **0.98**, ~100ms each way, skipped under reduced motion:

```csharp
        if (Helpers.Motion.Reduce)
            return;
        await card.ScaleTo(0.98, 100, Easing.CubicOut);
        await card.ScaleTo(1.0, 100, Easing.CubicOut);
```

(Preserve the method's existing structure around these lines; only the guard and values change.)

- [ ] **Step 4: Build and run tests**

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj -f net9.0-maccatalyst`
Expected: `Build succeeded`

Run: `dotnet test BuzzahBuddy.Tests`
Expected: all existing tests pass (this sweep touches no view-model logic).

- [ ] **Step 5: Commit**

```bash
git add BuzzahBuddy/Resources/Styles/Styles.xaml BuzzahBuddy/Helpers BuzzahBuddy/Views/GloveControlPage.xaml.cs
git commit -m "feat: switch thumb spec, reduced-motion support, press animation to spec"
```

---

### Task 7: Final compliance sweep + close out the spec's gap lists

**Files:**
- Modify: `docs/design/colors.md`, `docs/design/typography.md`, `docs/design/components.md` (gap sections)

- [ ] **Step 1: Forbidden-pair and leftovers audit**

Run each; expected **no output** (except noted):

```bash
# no light text paired with the blue fill in any view or style
grep -rn 'TextColor="{StaticResource TextPrimary}"' BuzzahBuddy --include='*.xaml' | grep -i primary
# no teal/green anywhere
grep -rniE '#14b8a6|#5eead4|green' BuzzahBuddy --include='*.xaml' --include='*.cs' | grep -v obj/
# no raw Danger token used as TextColor (must be DangerDark)
grep -rn 'TextColor="{StaticResource Danger}"' BuzzahBuddy/Views --include='*.xaml'
# no deleted tokens referenced
grep -rnE 'Pastel|Gray[0-9]|OpenSans|InfoBrush' BuzzahBuddy --include='*.xaml' --include='*.cs' | grep -v obj/
```

For the Danger-as-text check: `Views/DeviceListPage.xaml:66` has `TextColor="{StaticResource Danger}"` — change it to `DangerDark`. Fix anything else these greps surface.

- [ ] **Step 2: Update the design docs' gap sections**

In `docs/design/colors.md`, `typography.md`, and `components.md`, replace each "Current implementation gaps" section body with a one-liner: `All gaps above were remediated on 2026-07-14 (see docs/superpowers/plans/2026-07-14-design-system-remediation.md).` — keep the heading so old links resolve, and delete the now-false bullet claims.

- [ ] **Step 3: Full build of all targets**

Run: `dotnet build BuzzahBuddy/BuzzahBuddy.csproj`
Expected: `Build succeeded` for all TFMs (ios/android/maccatalyst; android may require SDK — if a target fails on missing workload/SDK rather than code, note it and rely on the maccatalyst build).

- [ ] **Step 4: Commit**

```bash
git add BuzzahBuddy docs/design
git commit -m "chore: final design-system compliance sweep, close out spec gap lists"
```
