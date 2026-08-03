using System.Threading.Tasks;
using Avalonia.Controls;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace SigXor;

public static class DialogHelper
{
    public static Task ShowErrorAsync(Window? owner, string message, string title = "错误") =>
        ShowAsync(owner, title, message, ButtonEnum.Ok, Icon.Error);

    public static Task ShowWarningAsync(Window? owner, string message, string title = "警告") =>
        ShowAsync(owner, title, message, ButtonEnum.Ok, Icon.Warning);

    public static Task ShowInfoAsync(Window? owner, string message, string title = "提示") =>
        ShowAsync(owner, title, message, ButtonEnum.Ok, Icon.Info);

    public static async Task<bool> ShowYesNoAsync(Window? owner, string message, string title = "确认")
    {
        var result = await ShowAsync(owner, title, message, ButtonEnum.YesNo, Icon.Question);
        return result == ButtonResult.Yes;
    }

    private static async Task<ButtonResult> ShowAsync(
        Window? owner,
        string title,
        string message,
        ButtonEnum buttons,
        Icon icon)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(title, message, buttons, icon);
        return owner != null
            ? await box.ShowWindowDialogAsync(owner)
            : await box.ShowAsync();
    }
}
