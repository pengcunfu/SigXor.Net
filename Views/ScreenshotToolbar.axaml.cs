using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;

namespace SigXor;

/// <summary>截屏工具条：OCR 识别 / 复制图片 / 保存 / 完成。</summary>
public partial class ScreenshotToolbar : Window
{
    private PixelRect _selectionRect;
    private Screen[] _screens = [];

    public event EventHandler? OcrRequested;
    public event EventHandler? CopyRequested;
    public event EventHandler? SaveRequested;
    public event EventHandler? CloseRequested;

    public ScreenshotToolbar()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    public void ShowAt(PixelRect selectionScreenRect, Screen[] screens)
    {
        _selectionRect = selectionScreenRect;
        _screens = screens;
        Show();
    }

    public void SetBusy(string message)
    {
        OcrButton.Content = message;
        OcrButton.IsEnabled = false;
        CopyButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        DoneButton.IsEnabled = false;
    }

    public void SetIdle()
    {
        OcrButton.Content = "OCR 识别";
        OcrButton.IsEnabled = true;
        CopyButton.IsEnabled = true;
        SaveButton.IsEnabled = true;
        DoneButton.IsEnabled = true;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        UpdateLayout();
        Reposition();
    }

    private void Reposition()
    {
        var w = (int)Math.Ceiling(Width);
        var h = (int)Math.Ceiling(Height);
        if (w <= 0 || h <= 0)
            return;

        var screen = _screens.FirstOrDefault(s => s.Bounds.Intersects(_selectionRect))
                     ?? _screens.FirstOrDefault()
                     ?? Screens.Primary;
        var area = screen?.Bounds ?? new PixelRect(0, 0, 1920, 1080);

        var x = _selectionRect.X + _selectionRect.Width / 2 - w / 2;
        var y = _selectionRect.Bottom + 8;
        if (y + h > area.Bottom && _selectionRect.Y - h - 8 >= area.Y)
            y = _selectionRect.Y - h - 8;

        x = Math.Clamp(x, area.X + 4, area.Right - w - 4);
        y = Math.Clamp(y, area.Y + 4, area.Bottom - h - 4);
        Position = new PixelPoint(x, y);
    }

    private void OnOcrClick(object? sender, RoutedEventArgs e) => OcrRequested?.Invoke(this, EventArgs.Empty);

    private void OnCopyClick(object? sender, RoutedEventArgs e) => CopyRequested?.Invoke(this, EventArgs.Empty);

    private void OnSaveClick(object? sender, RoutedEventArgs e) => SaveRequested?.Invoke(this, EventArgs.Empty);

    private void OnDoneClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
