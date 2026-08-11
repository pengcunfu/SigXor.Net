using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SigXor;

/// <summary>
/// 截图预览窗口：固定在选区位置按物理像素 1:1 显示裁剪图像，
/// 让用户在使用工具条时能直接看到高清截屏内容。
/// </summary>
public partial class RegionPreviewWindow : Window
{
    private PixelRect _screenRect;
    private double _targetScale = 1.0;
    private bool _dragging;
    private PixelPoint _pointerDownScreen;
    private PixelPoint _windowDownPosition;

    /// <summary>拖拽过程中的实时位置（物理像素），用于让工具条持续跟随。</summary>
    public event EventHandler<PixelPoint>? DragPositionChanged;

    public RegionPreviewWindow()
    {
        InitializeComponent();
        Cursor = new Cursor(StandardCursorType.SizeAll);
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
    }

    public void ShowAt(WriteableBitmap bitmap, PixelRect screenRect, double estimatedScale)
    {
        PreviewImage.Source = bitmap;
        RenderOptions.SetBitmapInterpolationMode(PreviewImage, BitmapInterpolationMode.None);
        _screenRect = screenRect;
        _targetScale = estimatedScale > 0 ? estimatedScale : 1.0;

        // 窗口 DIP 尺寸 = 物理像素 / Scaling，与位图 DPI(=96×Scaling) 对齐，避免二次缩放
        Width = screenRect.Width / _targetScale;
        Height = screenRect.Height / _targetScale;
        Position = new PixelPoint(screenRect.X, screenRect.Y);

        Opened += OnOpened;
        Show();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;

        // 优先用窗口真实 RenderScaling；若与目标屏不一致，仍按目标屏 Scaling 保持 1:1
        var scale = RenderScaling > 0 ? RenderScaling : _targetScale;
        if (Math.Abs(scale - _targetScale) > 0.01 && _targetScale > 0)
            scale = _targetScale;

        Width = _screenRect.Width / scale;
        Height = _screenRect.Height / scale;
        Position = new PixelPoint(_screenRect.X, _screenRect.Y);
        UpdateLayout();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        e.Handled = true;
        _dragging = true;
        _pointerDownScreen = Avalonia.VisualExtensions.PointToScreen(this, e.GetPosition(this));
        _windowDownPosition = Position;
        e.Pointer.Capture(DragSurface);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging)
            return;

        // 保险：即使 PointerReleased 未送达，也能检测到左键已松开
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragging = false;
            e.Pointer.Capture(null);
            DragPositionChanged?.Invoke(this, Position);
            return;
        }

        var currentScreen = Avalonia.VisualExtensions.PointToScreen(this, e.GetPosition(this));
        Position = new PixelPoint(
            _windowDownPosition.X + currentScreen.X - _pointerDownScreen.X,
            _windowDownPosition.Y + currentScreen.Y - _pointerDownScreen.Y);
        DragPositionChanged?.Invoke(this, Position);
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;
        e.Pointer.Capture(null);
        DragPositionChanged?.Invoke(this, Position);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // 窗口移动等导致系统捕获意外丢失时，立即停止拖拽，避免“松手后还跟着鼠标”
        _dragging = false;
    }
}
