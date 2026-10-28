using BuzzahBuddy.Models;

namespace BuzzahBuddy.Messages;

/// <summary>Sent after the app switches theme, for views that draw colors in code.</summary>
public sealed record ThemeChangedMessage(AppliedTheme Theme);
