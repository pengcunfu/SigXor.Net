using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SigXor;

/// <summary>
/// 区域截屏覆盖层：全虚拟桌面透明窗口，显示冻结画面，拖动鼠标选择区域。
/// 选择结果以位图像素坐标（物理像素，相对虚拟桌面原点）通过 SelectionConfirmed 返回。
/// </summary>
public partial class RegionCaptureOverlay : Window
{
    private readonly Bitmap _screenshot = null!;
    private readonly PixelRect _virtualBounds;
    private Point _startDip;
    private Point _endDip;
    private bool _dragging;
    private bool _settled;

    public event EventHandler<PixelRect>? SelectionConfirmed;
    public event EventHandler? SelectionCancelled;

    public RegionCaptureOverlay()
    {
        InitializeComponent();
    }

    public RegionCaptureOverlay(Bitmap screenshot, PixelRect virtualBounds, double estimatedScale = 1.0)
    {
        _screenshot = screenshot;
        _virtualBounds = virtualBounds;
        InitializeComponent();

        Position = new PixelPoint(_virtualBounds.X, _virtualBounds.Y);
        var scale = estimatedScale > 0 ? estimatedScale : 1.0;
        Width = _virtualBounds.Width / scale;
        Height = _virtualBounds.Height / scale;

        RootBorder.Background = new ImageBrush(screenshot) { Stretch = Stretch.Fill };
        OverlayCanvas.Cursor = new Cursor(StandardCursorType.Cross);

        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        // 窗口显示后才能拿到准确的渲染缩放，按物理像素精确对齐虚拟桌面
        var scale = RenderScaling > 0 ? RenderScaling : 1.0;
        Width = _virtualBounds.Width / scale;
        Height = _virtualBounds.Height / scale;
        Position = new PixelPoint(_virtualBounds.X, _virtualBounds.Y);
        UpdateLayout();
        PositionHint();
        Activate();
        Focus();
    }

    private double Scale => RenderScaling > 0 ? RenderScaling : 1.0;

    private void PositionHint()
    {
        var w = OverlayCanvas.Bounds.Width;
        Canvas.SetLeft(HintText, Math.Max(8, (w - HintText.Bounds.Width) / 2));
        Canvas.SetTop(HintText, 16);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_settled)
            return;

        var props = e.GetCurrentPoint(OverlayCanvas).Properties;
        if (props.IsRightButtonPressed)
        {
            Cancel();
            e.Handled = true;
            return;
        }

        if (!props.IsLeftButtonPressed)
            return;

        _startDip = e.GetPosition(OverlayCanvas);
        _endDip = _startDip;
        _dragging = true;
        e.Pointer.Capture(OverlayCanvas);
        UpdateSelectionVisual();
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging)
            return;

        _endDip = e.GetPosition(OverlayCanvas);
        UpdateSelectionVisual();
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;
        e.Pointer.Capture(null);
        _endDip = e.GetPosition(OverlayCanvas);
        UpdateSelectionVisual();

        var rect = GetSelectionPhysical();
        if (rect.Width >= 4 && rect.Height >= 4)
            Confirm(rect);
        else
        {
            // 选区过小视为误操作，清空后允许重新选择
            _startDip = _endDip = default;
            UpdateSelectionVisual();
        }

        e.Handled = true;
    }

    private PixelRect GetSelectionPhysical()
    {
        var scale = Scale;
        var x0 = (int)Math.Round(Math.Min(_startDip.X, _endDip.X) * scale);
        var y0 = (int)Math.Round(Math.Min(_startDip.Y, _endDip.Y) * scale);
        var x1 = (int)Math.Round(Math.Max(_startDip.X, _endDip.X) * scale);
        var y1 = (int)Math.Round(Math.Max(_startDip.Y, _endDip.Y) * scale);
        return new PixelRect(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    private void UpdateSelectionVisual()
    {
        var left = Math.Min(_startDip.X, _endDip.X);
        var top = Math.Min(_startDip.Y, _endDip.Y);
        var width = Math.Abs(_endDip.X - _startDip.X);
        var height = Math.Abs(_endDip.Y - _startDip.Y);
        var canvasW = OverlayCanvas.Bounds.Width;
        var canvasH = OverlayCanvas.Bounds.Height;

        if (canvasW <= 0 || canvasH <= 0 || width < 1 || height < 1)
        {
            DimTop.IsVisible = DimBottom.IsVisible = DimLeft.IsVisible = DimRight.IsVisible = false;
            SelectionBorder.IsVisible = SelectionLabel.IsVisible = false;
            return;
        }

        Canvas.SetLeft(DimTop, 0);
        Canvas.SetTop(DimTop, 0);
        DimTop.Width = canvasW;
        DimTop.Height = top;

        Canvas.SetLeft(DimBottom, 0);
        Canvas.SetTop(DimBottom, top + height);
        DimBottom.Width = canvasW;
        DimBottom.Height = canvasH - top - height;

        Canvas.SetLeft(DimLeft, 0);
        Canvas.SetTop(DimLeft, top);
        DimLeft.Width = left;
        DimLeft.Height = height;

        Canvas.SetLeft(DimRight, left + width);
        Canvas.SetTop(DimRight, top);
        DimRight.Width = canvasW - left - width;
        DimRight.Height = height;

        Canvas.SetLeft(SelectionBorder, left);
        Canvas.SetTop(SelectionBorder, top);
        SelectionBorder.Width = width;
        SelectionBorder.Height = height;

        var phys = GetSelectionPhysical();
        SelectionLabel.Text = $"{phys.Width} × {phys.Height}";
        var labelX = left;
        var labelY = top - 24;
        if (labelY < 4)
            labelY = top + height + 4;
        Canvas.SetLeft(SelectionLabel, labelX);
        Canvas.SetTop(SelectionLabel, labelY);

        DimTop.IsVisible = DimBottom.IsVisible = DimLeft.IsVisible = DimRight.IsVisible = true;
        SelectionBorder.IsVisible = SelectionLabel.IsVisible = true;
    }

    private void Confirm(PixelRect rect)
    {
        if (_settled)
            return;

        _settled = true;
        SelectionConfirmed?.Invoke(this, rect);
    }

    private void Cancel()
    {
        if (_settled)
            return;

        _settled = true;
        SelectionCancelled?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Enter or Key.Return && !_dragging)
        {
            var rect = GetSelectionPhysical();
            if (rect.Width >= 4 && rect.Height >= 4)
            {
                Confirm(rect);
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (!_settled)
            SelectionCancelled?.Invoke(this, EventArgs.Empty);
        base.OnClosed(e);
    }
}
