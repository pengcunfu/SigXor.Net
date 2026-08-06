using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;

namespace SigXor;

/// <summary>
/// 截图预览窗口：固定在选区位置显示裁剪出的图像（1:1 物理像素），
/// 让用户在使用工具条时能直接看到截屏内容。
/// </summary>
public partial class RegionPreviewWindow : Window
{
    private const int WmNclbuttondown = 0x00A1;
    private const int HtCaption = 0x0002;

    private PixelRect _screenRect;
    private bool _dragging;
    private Point _pointerDownPosition;
    private PixelPoint _windowDownPosition;

    public RegionPreviewWindow()
    {
        InitializeComponent();
        Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    public void ShowAt(WriteableBitmap bitmap, PixelRect screenRect, double estimatedScale)
    {
        PreviewImage.Source = bitmap;
        _screenRect = screenRect;

        var scale = estimatedScale > 0 ? estimatedScale : 1.0;
        Width = screenRect.Width / scale;
        Height = screenRect.Height / scale;
        Position = new PixelPoint(screenRect.X, screenRect.Y);

        Opened += OnOpened;
        Show();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        // 显示后按真实渲染缩放精确对齐选区位置
        var scale = RenderScaling > 0 ? RenderScaling : 1.0;
        Width = _screenRect.Width / scale;
        Height = _screenRect.Height / scale;
        Position = new PixelPoint(_screenRect.X, _screenRect.Y);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        e.Handled = true;

        if (OperatingSystem.IsWindows())
        {
            // 交给系统原生拖拽：无抖动、无闪烁，跨显示器也平滑
            BeginNativeDrag();
            return;
        }

        _dragging = true;
        _pointerDownPosition = e.GetPosition(this);
        _windowDownPosition = Position;
        e.Pointer.Capture(this);
    }

    private void BeginNativeDrag()
    {
        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
            return;

        ReleaseCapture();
        SendMessage(handle, WmNclbuttondown, (IntPtr)HtCaption, IntPtr.Zero);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging)
            return;

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

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
