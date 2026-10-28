using BuzzahBuddy.Helpers;
using BuzzahBuddy.Views;

namespace BuzzahBuddy.Services.Dialogs;

public sealed class ConfirmationService : IConfirmationService
{
    public async Task<bool> ConfirmAsync(ConfirmRequest request)
    {
        if (Application.Current?.Windows.FirstOrDefault()?.Page is not { } root)
            return false;

        var sheet = new ConfirmSheetPage(request);
        await root.Navigation.PushModalAsync(sheet, animated: !Motion.Reduce);
        var confirmed = await sheet.Result;
        if (root.Navigation.ModalStack.Contains(sheet))
            await root.Navigation.PopModalAsync(animated: !Motion.Reduce);
        return confirmed;
    }
}
