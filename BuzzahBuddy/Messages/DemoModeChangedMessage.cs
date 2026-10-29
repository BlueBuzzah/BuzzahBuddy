namespace BuzzahBuddy.Messages;

/// <summary>Sent after Demo Mode switches between real and pretend gloves.</summary>
public sealed record DemoModeChangedMessage(bool DemoMode);
