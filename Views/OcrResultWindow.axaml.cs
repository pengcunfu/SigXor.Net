using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SigXor;

/// <summary>
/// OCR 识别结果对话框：点击识别时立即弹出并显示进度，识别完成后展示结果，
/// 由用户选择「复制」或关闭。无最小化/最大化按钮。
/// </summary>
public partial class OcrResultWindow : Window
{
    private const int WmNclbuttondown = 0x00A1;
    private const int HtCaption = 0x0002;

    private PixelRect _anchorRect;
    private Screen[] _screens = [];
    private bool _hasAnchor;

    public OcrResultWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Dispatcher.UIThread.Post(RepositionToAnchorScreen, DispatcherPriority.Loaded);
    }

    /// <summary>立即弹出窗口并显示忙碌状态（开始识别时调用）。</summary>
    public void ShowBusy(string message, PixelRect? anchorScreenRect = null, Screen[]? screens = null)
    {
        CopyButton.IsEnabled = false;
        SetStatus(message, success: false);
        MetaText.Text = string.Empty;
        ResultText.Text = string.Empty;

        if (anchorScreenRect.HasValue)
        {
            _anchorRect = anchorScreenRect.Value;
            _hasAnchor = true;
        }

        _screens = screens ?? [];

        Show();
        Activate();
        Focus();
        RepositionToAnchorScreen();
    }

    /// <summary>更新识别进度（模型下载 / 加载 / 识别中）。</summary>
    public void SetBusy(string message) => SetStatus(message, success: false);

    /// <summary>识别完成，展示结果并允许复制。</summary>
    public void ShowResult(string text)
    {
        var content = text ?? string.Empty;
        SetStatus("识别完成", success: true);
        ResultText.Text = content;
        CopyButton.IsEnabled = !string.IsNullOrWhiteSpace(content);
        CopyButton.Content = "复制文本";
        MetaText.Text = BuildMeta(content);
        Dispatcher.UIThread.Post(RepositionToAnchorScreen, DispatcherPriority.Loaded);
    }

    /// <summary>识别失败或无结果，展示提示。</summary>
    public void ShowFailure(string message)
    {
        SetStatus(message, success: false);
        CopyButton.IsEnabled = false;
        CopyButton.Content = "复制文本";
        MetaText.Text = string.Empty;
        Dispatcher.UIThread.Post(RepositionToAnchorScreen, DispatcherPriority.Loaded);
    }

    /// <summary>把窗口相对预览框居中，并限制在屏幕工作区内。</summary>
    private void RepositionToAnchorScreen()
    {
        UpdateLayout();

        var scale = RenderScaling > 0 ? RenderScaling : 1.0;
        var width = Bounds.Width > 0 ? Bounds.Width : Width;
        var height = Bounds.Height > 0 ? Bounds.Height : Height;
        if (width <= 0) width = 440;
        if (height <= 0) height = 280;

        var widthPx = (int)Math.Ceiling(width * scale);
        var heightPx = (int)Math.Ceiling(height * scale);

        var screen = ResolveTargetScreen();
        var area = screen?.WorkingArea ?? screen?.Bounds
                   ?? new PixelRect(0, 0, 1920, 1080);

        int x;
        int y;
        if (_hasAnchor)
        {
            // 相对图片预览框中心对齐
            x = _anchorRect.X + (_anchorRect.Width - widthPx) / 2;
            y = _anchorRect.Y + (_anchorRect.Height - heightPx) / 2;
        }
        else
        {
            x = area.X + Math.Max(0, (area.Width - widthPx) / 2);
            y = area.Y + Math.Max(0, (area.Height - heightPx) / 2);
        }

        x = Math.Clamp(x, area.X + 8, Math.Max(area.X + 8, area.Right - widthPx - 8));
        y = Math.Clamp(y, area.Y + 8, Math.Max(area.Y + 8, area.Bottom - heightPx - 8));
        Position = new PixelPoint(x, y);
    }

    private Screen? ResolveTargetScreen()
    {
        if (_hasAnchor && _screens.Length > 0)
        {
            var center = new PixelPoint(
                _anchorRect.X + Math.Max(1, _anchorRect.Width) / 2,
                _anchorRect.Y + Math.Max(1, _anchorRect.Height) / 2);

            return _screens.FirstOrDefault(s => s.Bounds.Contains(center))
                   ?? _screens.FirstOrDefault(s => s.Bounds.Intersects(_anchorRect))
                   ?? _screens.FirstOrDefault();
        }

        return Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
    }

    private void SetStatus(string message, bool success)
    {
        StatusText.Text = message;
        StatusText.Foreground = success
            ? new SolidColorBrush(Color.Parse("#2F6B4F"))
            : new SolidColorBrush(Color.Parse("#5B6577"));

        if (StatusText.Parent is Border badge)
        {
            badge.Background = success
                ? new SolidColorBrush(Color.Parse("#E7F5EE"))
                : new SolidColorBrush(Color.Parse("#EDF2F7"));
        }
    }

    private static string BuildMeta(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        var chars = text.Replace("\r", "").Replace("\n", "").Length;
        return $"{chars} 字 · {lines} 行";
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
        if (IsInsideButton(e.Source))
            return;

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

    private static bool IsInsideButton(object? source)
    {
        var element = source as Visual;
        while (element != null)
        {
            if (element is Button)
                return true;
            element = element.Parent as Visual;
        }

        return false;
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
