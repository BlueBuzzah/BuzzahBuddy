using BuzzahBuddy.Views;

namespace BuzzahBuddy.Services.Dialogs;

public sealed class ConfirmationService : IConfirmationService
{
    public async Task<bool> ConfirmAsync(ConfirmRequest request)
    {
        if (Application.Current?.Windows.FirstOrDefault()?.Page is not { } root)
            return false;

        var sheet = new ConfirmSheetPage(request);
        // Unanimated: the sheet slides itself so the scrim doesn't slide with it.
        await root.Navigation.PushModalAsync(sheet, animated: false);
        var confirmed = await sheet.Result;
        if (root.Navigation.ModalStack.Contains(sheet))
        {
            await sheet.SlideOutAsync();
            await root.Navigation.PopModalAsync(animated: false);
        }
        return confirmed;
    }
}
