using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SigXor;

/// <summary>截屏工具条：OCR 识别 / 复制图片 / 保存 / 完成。</summary>
public partial class ScreenshotToolbar : Window
{
    private PixelRect _selectionRect;
    private Screen[] _screens = [];
    private readonly DispatcherTimer _toastTimer;
    private bool _dragging;
    private Point _pointerDownPosition;
    private PixelPoint _windowDownPosition;

    public event EventHandler? OcrRequested;
    public event EventHandler? ColorPickRequested;
    public event EventHandler? CopyRequested;
    public event EventHandler? SaveRequested;
    public event EventHandler? CloseRequested;

    public ScreenshotToolbar()
    {
        InitializeComponent();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
        _toastTimer.Tick += OnToastTimerTick;
        Cursor = new Cursor(StandardCursorType.SizeAll);
        OcrButton.Cursor = new Cursor(StandardCursorType.Arrow);
        ColorPickButton.Cursor = new Cursor(StandardCursorType.Arrow);
        CopyButton.Cursor = new Cursor(StandardCursorType.Arrow);
        SaveButton.Cursor = new Cursor(StandardCursorType.Arrow);
        DoneButton.Cursor = new Cursor(StandardCursorType.Arrow);
        Opened += OnOpened;
    }

    /// <summary>在工具条上短暂显示一条操作结果提示。</summary>
    public void ShowToast(string message)
    {
        StatusText.Text = message;
        StatusText.IsVisible = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    public void ShowAt(PixelRect selectionScreenRect, Screen[] screens)
    {
        _selectionRect = selectionScreenRect;
        _screens = screens;
        Show();
    }

    /// <summary>把工具条移动到指定选区附近（优先下方，空间不足则上方），并限制在屏幕内。</summary>
    public void MoveNear(PixelRect anchor)
    {
        _selectionRect = anchor;
        Reposition();
    }

    public void SetBusy(string message)
    {
        OcrButton.Content = message;
        OcrButton.IsEnabled = false;
        ColorPickButton.IsEnabled = false;
        CopyButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        DoneButton.IsEnabled = false;
        _toastTimer.Stop();
        StatusText.Text = message;
        StatusText.IsVisible = true;
    }

    public void SetIdle()
    {
        OcrButton.Content = "OCR 识别";
        OcrButton.IsEnabled = true;
        ColorPickButton.IsEnabled = true;
        CopyButton.IsEnabled = true;
        SaveButton.IsEnabled = true;
        DoneButton.IsEnabled = true;
        _toastTimer.Stop();
        StatusText.Text = string.Empty;
        StatusText.IsVisible = false;
    }

    private void OnToastTimerTick(object? sender, EventArgs e)
    {
        _toastTimer.Stop();
        StatusText.Text = string.Empty;
        StatusText.IsVisible = false;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        UpdateLayout();
        Reposition();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsInsideButton(e.Source))
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        e.Handled = true;

        if (OperatingSystem.IsWindows())
        {
            // 系统原生拖拽：无抖动、无闪烁
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero)
                return;

            ReleaseCapture();
            SendMessage(handle, WmNclbuttondown, (IntPtr)HtCaption, IntPtr.Zero);
            return;
        }

        _dragging = true;
        _pointerDownPosition = e.GetPosition(this);
        _windowDownPosition = Position;
        e.Pointer.Capture(ToolbarSurface);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragging = false;
            e.Pointer.Capture(null);
            return;
        }

        var current = e.GetPosition(this);
        var scale = RenderScaling > 0 ? RenderScaling : 1.0;
        var dx = (int)Math.Round((current.X - _pointerDownPosition.X) * scale);
        var dy = (int)Math.Round((current.Y - _pointerDownPosition.Y) * scale);
        Position = new PixelPoint(_windowDownPosition.X + dx, _windowDownPosition.Y + dy);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _dragging = false;
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

    private const int WmNclbuttondown = 0x00A1;
    private const int HtCaption = 0x0002;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private void Reposition()
    {
        var scale = RenderScaling > 0 ? RenderScaling : 1.0;
        var w = (int)Math.Ceiling(Width * scale);
        var h = (int)Math.Ceiling(Height * scale);
        if (w <= 0 || h <= 0)
            return;

        var screen = _screens.FirstOrDefault(s => s.Bounds.Intersects(_selectionRect))
                     ?? _screens.FirstOrDefault()
                     ?? Screens.Primary;
        var area = screen?.Bounds ?? new PixelRect(0, 0, 1920, 1080);

        var x = _selectionRect.X + _selectionRect.Width / 2 - w / 2;
        var y = _selectionRect.Bottom + 8; // 始终在预览框下方固定距离
        x = Math.Clamp(x, area.X + 4, area.Right - w - 4);
        y = Math.Clamp(y, area.Y + 4, area.Bottom - h - 4);
        Position = new PixelPoint(x, y);
    }

    private void OnOcrClick(object? sender, RoutedEventArgs e) => OcrRequested?.Invoke(this, EventArgs.Empty);

    private void OnColorPickClick(object? sender, RoutedEventArgs e) => ColorPickRequested?.Invoke(this, EventArgs.Empty);

    private void OnCopyClick(object? sender, RoutedEventArgs e) => CopyRequested?.Invoke(this, EventArgs.Empty);

    private void OnSaveClick(object? sender, RoutedEventArgs e) => SaveRequested?.Invoke(this, EventArgs.Empty);

    private void OnDoneClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
