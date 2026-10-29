namespace BuzzahBuddy.Services.Dialogs;

/// <summary>One confirmation: what happens, and the words on both buttons.</summary>
public sealed record ConfirmRequest(
    string Title, string Message, string ConfirmText, string CancelText = "Cancel", bool Destructive = false);

/// <summary>
/// Asks before an action with a bottom sheet whose confirm button sits apart from Cancel
/// (accessibility.md Motor). Anything but the confirm button, including Android back,
/// counts as Cancel.
/// </summary>
public interface IConfirmationService
{
    Task<bool> ConfirmAsync(ConfirmRequest request);
}
