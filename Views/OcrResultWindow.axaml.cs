using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SigXor;

/// <summary>
/// OCR 识别结果对话框：点击识别时立即弹出并显示进度，识别完成后展示结果，
/// 由用户选择「复制」或「不复制」。无最小化/最大化按钮。
/// </summary>
public partial class OcrResultWindow : Window
{
    private const int WmNclbuttondown = 0x00A1;
    private const int HtCaption = 0x0002;

    public OcrResultWindow()
    {
        InitializeComponent();
    }

    /// <summary>立即弹出窗口并显示忙碌状态（开始识别时调用）。</summary>
    public void ShowBusy(string message)
    {
        CopyButton.IsEnabled = false;
        StatusText.Text = message;
        ResultText.Text = string.Empty;
        Show();
        Activate();
        Focus();
    }

    /// <summary>更新识别进度（模型下载 / 加载 / 识别中）。</summary>
    public void SetBusy(string message) => StatusText.Text = message;

    /// <summary>识别完成，展示结果并允许复制。</summary>
    public void ShowResult(string text)
    {
        StatusText.Text = "识别完成";
        ResultText.Text = text ?? string.Empty;
        CopyButton.IsEnabled = true;
        CopyButton.Content = "复制";
    }

    /// <summary>识别失败或无结果，展示提示。</summary>
    public void ShowFailure(string message)
    {
        StatusText.Text = message;
        CopyButton.IsEnabled = false;
        CopyButton.Content = "复制";
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        var text = ResultText.Text;
        if (string.IsNullOrEmpty(text))
        {
            Close();
            return;
        }

        try
        {
            if (OperatingSystem.IsWindows())
                WindowsClipboardHelper.SetText(text);
            else
                await CopyViaClipboardAsync(text);

            Close();
        }
        catch (Exception ex)
        {
            CopyButton.Content = "复制失败";
            CopyButton.IsEnabled = false;
            System.Diagnostics.Debug.WriteLine($"OCR 结果复制失败: {ex.Message}");
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnTitlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        e.Handled = true;
        if (!OperatingSystem.IsWindows())
            return;

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
            return;

        ReleaseCapture();
        SendMessage(handle, WmNclbuttondown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private async Task CopyViaClipboardAsync(string text)
    {
        var clipboard = Clipboard ?? TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
            throw new InvalidOperationException("无法访问剪贴板");
        await clipboard.SetTextAsync(text);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
