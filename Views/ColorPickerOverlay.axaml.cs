using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SigXor;

/// <summary>
/// Snipaste 风格取色器：冻结画面 + 像素放大镜，支持 HEX/RGB 切换与复制。
/// </summary>
public partial class ColorPickerOverlay : Window
{
    private const int SampleRadius = 7; // 中心两侧各 7 像素 → 15×15
    private const int CellSize = 10;    // 每源像素放大到 10px
    private const int MagPixelSize = (SampleRadius * 2 + 1) * CellSize;

    private WriteableBitmap? _freeze;
    private PixelRect _virtualBounds;
    private double _scale = 1.0;
    private WriteableBitmap? _magnifierBitmap;
    private bool _useHex = true;
    private byte _r, _g, _b;
    private int _cursorX;
    private int _cursorY;
    private bool _copiedFlash;
    private readonly DispatcherTimer _flashTimer;

    public event EventHandler<string>? ColorCopied;
    public event EventHandler? Cancelled;

    public ColorPickerOverlay()
    {
        InitializeComponent();
        Cursor = new Cursor(StandardCursorType.Cross);
        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
        _flashTimer.Tick += (_, _) =>
        {
            _flashTimer.Stop();
            _copiedFlash = false;
            UpdateColorLabels();
        };

        KeyDown += OnKeyDown;
        Opened += OnOpened;
    }

    public void ShowAt(WriteableBitmap freeze, PixelRect virtualBounds, double estimatedScale)
    {
        _freeze = freeze;
        _virtualBounds = virtualBounds;
        _scale = estimatedScale > 0 ? estimatedScale : 1.0;

        FreezeBackground.Background = new ImageBrush(freeze)
        {
            Stretch = Stretch.Fill
        };
        RenderOptions.SetBitmapInterpolationMode(FreezeBackground, BitmapInterpolationMode.None);

        Position = new PixelPoint(virtualBounds.X, virtualBounds.Y);
        Width = virtualBounds.Width / _scale;
        Height = virtualBounds.Height / _scale;

        _magnifierBitmap = new WriteableBitmap(
            new PixelSize(MagPixelSize, MagPixelSize),
            new Vector(96, 96),
            PixelFormats.Bgra8888,
            AlphaFormat.Opaque);
        MagnifierImage.Source = _magnifierBitmap;
        MagnifierImage.Width = MagPixelSize;
        MagnifierImage.Height = MagPixelSize;

        Show();
        Activate();
        Focus();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        var scale = RenderScaling > 0 ? RenderScaling : _scale;
        _scale = scale > 0 ? scale : 1.0;
        Width = _virtualBounds.Width / _scale;
        Height = _virtualBounds.Height / _scale;
        Position = new PixelPoint(_virtualBounds.X, _virtualBounds.Y);
        UpdateLayout();

        var screenPos = GetCursorScreenPos();
        UpdateFromScreenPoint(screenPos);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var screen = Avalonia.VisualExtensions.PointToScreen(this, e.GetPosition(this));
        UpdateFromScreenPoint(screen);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
            Close();
            e.Handled = true;
            return;
        }

        if (!props.IsLeftButtonPressed)
            return;

        CopyCurrentColor();
        e.Handled = true;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        // 预留：滚轮可扩展放大倍率；当前保持固定像素级放大
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Cancelled?.Invoke(this, EventArgs.Empty);
                Close();
                e.Handled = true;
                break;

            case Key.C:
                CopyCurrentColor();
                e.Handled = true;
                break;

            case Key.LeftShift:
            case Key.RightShift:
                _useHex = !_useHex;
                UpdateColorLabels();
                e.Handled = true;
                break;

            case Key.W:
            case Key.Up:
                NudgeCursor(0, -1);
                e.Handled = true;
                break;
            case Key.S:
            case Key.Down:
                NudgeCursor(0, 1);
                e.Handled = true;
                break;
            case Key.A:
            case Key.Left:
                NudgeCursor(-1, 0);
                e.Handled = true;
                break;
            case Key.D:
            case Key.Right:
                NudgeCursor(1, 0);
                e.Handled = true;
                break;
        }
    }

    private void NudgeCursor(int dx, int dy)
    {
        _cursorX = Math.Clamp(_cursorX + dx, 0, Math.Max(0, (_freeze?.PixelSize.Width ?? 1) - 1));
        _cursorY = Math.Clamp(_cursorY + dy, 0, Math.Max(0, (_freeze?.PixelSize.Height ?? 1) - 1));
        RefreshMagnifier();
        PositionMagnifierCard();
    }

    private void UpdateFromScreenPoint(PixelPoint screen)
    {
        if (_freeze == null)
            return;

        var x = screen.X - _virtualBounds.X;
        var y = screen.Y - _virtualBounds.Y;
        _cursorX = Math.Clamp(x, 0, Math.Max(0, _freeze.PixelSize.Width - 1));
        _cursorY = Math.Clamp(y, 0, Math.Max(0, _freeze.PixelSize.Height - 1));
        RefreshMagnifier();
        PositionMagnifierCard(screen);
    }

    private void RefreshMagnifier()
    {
        if (_freeze == null || _magnifierBitmap == null)
            return;

        SamplePixel(_freeze, _cursorX, _cursorY, out _r, out _g, out _b);
        RenderMagnifier(_freeze, _cursorX, _cursorY, _magnifierBitmap);
        MagnifierImage.InvalidateVisual();

        ColorSwatch.Background = new SolidColorBrush(Color.FromRgb(_r, _g, _b));
        UpdateColorLabels();
    }

    private void UpdateColorLabels()
    {
        var value = FormatColor(_r, _g, _b, _useHex);
        ColorValueText.Text = _copiedFlash ? $"已复制 {value}" : value;
        HintText.Text = _useHex
            ? "点击/C 复制 · Shift→RGB · Esc 退出"
            : "点击/C 复制 · Shift→HEX · Esc 退出";
    }

    private void CopyCurrentColor()
    {
        var value = FormatColor(_r, _g, _b, _useHex);
        try
        {
            if (OperatingSystem.IsWindows())
                WindowsClipboardHelper.SetText(value);
            else
            {
                // 非 Windows 时尽力写入 Avalonia 剪贴板
                _ = Clipboard?.SetTextAsync(value);
            }

            _copiedFlash = true;
            UpdateColorLabels();
            _flashTimer.Stop();
            _flashTimer.Start();
            ColorCopied?.Invoke(this, value);
        }
        catch (Exception ex)
        {
            HintText.Text = $"复制失败: {ex.Message}";
        }
    }

    private void PositionMagnifierCard(PixelPoint? screenCursor = null)
    {
        var scale = _scale > 0 ? _scale : 1.0;
        var cardW = MagnifierCard.Bounds.Width > 0 ? MagnifierCard.Bounds.Width : 184;
        var cardH = MagnifierCard.Bounds.Height > 0 ? MagnifierCard.Bounds.Height : 220;

        double cursorDipX;
        double cursorDipY;
        if (screenCursor.HasValue)
        {
            cursorDipX = (screenCursor.Value.X - _virtualBounds.X) / scale;
            cursorDipY = (screenCursor.Value.Y - _virtualBounds.Y) / scale;
        }
        else
        {
            cursorDipX = _cursorX / scale;
            cursorDipY = _cursorY / scale;
        }

        var winW = Bounds.Width > 0 ? Bounds.Width : Width;
        var winH = Bounds.Height > 0 ? Bounds.Height : Height;

        // 默认放在光标右下，空间不足则翻到左/上
        var x = cursorDipX + 24;
        var y = cursorDipY + 24;
        if (x + cardW > winW - 8)
            x = cursorDipX - cardW - 24;
        if (y + cardH > winH - 8)
            y = cursorDipY - cardH - 24;
        x = Math.Clamp(x, 8, Math.Max(8, winW - cardW - 8));
        y = Math.Clamp(y, 8, Math.Max(8, winH - cardH - 8));

        MagnifierCard.Margin = new Thickness(x, y, 0, 0);
    }

    private static string FormatColor(byte r, byte g, byte b, bool hex) =>
        hex ? $"#{r:X2}{g:X2}{b:X2}" : $"RGB({r}, {g}, {b})";

    private static void SamplePixel(WriteableBitmap source, int x, int y, out byte r, out byte g, out byte b)
    {
        using var fb = source.Lock();
        var offset = y * fb.RowBytes + x * 4;
        b = Marshal.ReadByte(fb.Address, offset);
        g = Marshal.ReadByte(fb.Address, offset + 1);
        r = Marshal.ReadByte(fb.Address, offset + 2);
    }

    private static void RenderMagnifier(WriteableBitmap source, int cx, int cy, WriteableBitmap target)
    {
        var srcW = source.PixelSize.Width;
        var srcH = source.PixelSize.Height;

        using var src = source.Lock();
        using var dst = target.Lock();
        var srcStride = src.RowBytes;
        var dstStride = dst.RowBytes;

        for (var gy = -SampleRadius; gy <= SampleRadius; gy++)
        {
            for (var gx = -SampleRadius; gx <= SampleRadius; gx++)
            {
                var sx = Math.Clamp(cx + gx, 0, srcW - 1);
                var sy = Math.Clamp(cy + gy, 0, srcH - 1);
                var srcOff = sy * srcStride + sx * 4;
                var b = Marshal.ReadByte(src.Address, srcOff);
                var g = Marshal.ReadByte(src.Address, srcOff + 1);
                var r = Marshal.ReadByte(src.Address, srcOff + 2);

                // 轻微网格分隔，便于看清单像素
                var grid = (gx + SampleRadius + gy + SampleRadius) % 2 == 0;
                if (grid)
                {
                    r = (byte)Math.Min(255, r + 6);
                    g = (byte)Math.Min(255, g + 6);
                    b = (byte)Math.Min(255, b + 6);
                }

                var dx0 = (gx + SampleRadius) * CellSize;
                var dy0 = (gy + SampleRadius) * CellSize;
                for (var py = 0; py < CellSize; py++)
                {
                    for (var px = 0; px < CellSize; px++)
                    {
                        // 单元格边缘画细线
                        var edge = px == 0 || py == 0;
                        var outR = edge ? (byte)Math.Max(0, r - 28) : r;
                        var outG = edge ? (byte)Math.Max(0, g - 28) : g;
                        var outB = edge ? (byte)Math.Max(0, b - 28) : b;
                        var dstOff = (dy0 + py) * dstStride + (dx0 + px) * 4;
                        Marshal.WriteByte(dst.Address, dstOff, outB);
                        Marshal.WriteByte(dst.Address, dstOff + 1, outG);
                        Marshal.WriteByte(dst.Address, dstOff + 2, outR);
                        Marshal.WriteByte(dst.Address, dstOff + 3, 255);
                    }
                }
            }
        }
    }

    private static PixelPoint GetCursorScreenPos()
    {
        GetCursorPos(out var pt);
        return new PixelPoint(pt.X, pt.Y);
    }

    protected override void OnClosed(EventArgs e)
    {
        _flashTimer.Stop();
        _magnifierBitmap?.Dispose();
        _magnifierBitmap = null;
        _freeze?.Dispose();
        _freeze = null;
        base.OnClosed(e);
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out PointApi lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointApi
    {
        public int X;
        public int Y;
    }
}
