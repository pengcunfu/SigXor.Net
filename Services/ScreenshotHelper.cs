using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SigXor;

/// <summary>全屏截屏（仅 Windows），支持保存 PNG 与复制到剪贴板（CF_BITMAP / CF_DIB）。</summary>
public static class ScreenshotHelper
{
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>捕获所有显示器的整个虚拟桌面，返回 Avalonia 位图（用于保存 PNG）。</summary>
    public static WriteableBitmap? CaptureFullScreen()
    {
        using var capture = CaptureToDib();
        if (capture == null)
            return null;

        var pixels = new byte[capture.Stride * capture.Height];
        Marshal.Copy(capture.BitsPtr, pixels, 0, pixels.Length);

        var bitmap = new WriteableBitmap(
            new PixelSize(capture.Width, capture.Height),
            new Vector(96, 96),
            PixelFormats.Bgra8888,
            AlphaFormat.Opaque);

        using (var framebuffer = bitmap.Lock())
        {
            if (framebuffer.RowBytes == capture.Stride)
            {
                Marshal.Copy(pixels, 0, framebuffer.Address, pixels.Length);
            }
            else
            {
                for (var row = 0; row < capture.Height; row++)
                {
                    Marshal.Copy(pixels, row * capture.Stride,
                        framebuffer.Address + row * framebuffer.RowBytes, capture.Stride);
                }
            }
        }

        return bitmap;
    }

    /// <summary>截取整屏并复制到系统剪贴板。成功时剪贴板接管位图句柄。</summary>
    public static bool CopyScreenToClipboard()
    {
        using var capture = CaptureToDib();
        if (capture == null)
            return false;

        return CopyDibToClipboard(capture);
    }

    /// <summary>把指定截图位图复制到系统剪贴板（CF_BITMAP / CF_DIB）。</summary>
    public static bool CopyBitmapToClipboard(WriteableBitmap bitmap)
    {
        if (bitmap == null)
            return false;

        using var capture = CreateCaptureFromBitmap(bitmap);
        if (capture == null)
            return false;

        return CopyDibToClipboard(capture);
    }

    /// <summary>从整屏/整图位图中裁剪出指定区域，返回新的 WriteableBitmap（BGRA）。</summary>
    /// <param name="dpi">显示 DPI；高分屏预览应传 96×Scaling，才能按物理像素 1:1 清晰显示。</param>
    public static WriteableBitmap? CropBitmap(WriteableBitmap source, PixelRect rect, Vector? dpi = null)
    {
        if (source == null)
            return null;

        var sourceSize = source.PixelSize;
        var clipped = rect.Intersect(new PixelRect(0, 0, sourceSize.Width, sourceSize.Height));
        if (clipped.Width <= 0 || clipped.Height <= 0)
            return null;

        var bitmapDpi = dpi ?? new Vector(96, 96);
        if (bitmapDpi.X <= 0 || bitmapDpi.Y <= 0)
            bitmapDpi = new Vector(96, 96);

        var cropped = new WriteableBitmap(
            new PixelSize(clipped.Width, clipped.Height),
            bitmapDpi,
            PixelFormats.Bgra8888,
            AlphaFormat.Opaque);

        var rowBytes = clipped.Width * 4;
        var buffer = new byte[rowBytes];
        using var src = source.Lock();
        using var dst = cropped.Lock();
        var srcRowBytes = src.RowBytes;
        var dstRowBytes = dst.RowBytes;

        for (var row = 0; row < clipped.Height; row++)
        {
            var srcAddress = src.Address + (clipped.Y + row) * srcRowBytes + clipped.X * 4;
            Marshal.Copy(srcAddress, buffer, 0, rowBytes);
            Marshal.Copy(buffer, 0, dst.Address + row * dstRowBytes, rowBytes);
        }

        return cropped;
    }

    private static bool CopyDibToClipboard(ScreenCapture capture)
    {
        if (!OpenClipboard(IntPtr.Zero))
            return false;

        try
        {
            if (!EmptyClipboard())
                return false;

            var bitmapOwnedByClipboard = false;
            try
            {
                // CF_BITMAP：GDI 位图（经典格式）
                bitmapOwnedByClipboard = SetClipboardData(CF_Bitmap, capture.HBitmap) != IntPtr.Zero;
                if (bitmapOwnedByClipboard)
                    capture.HBitmap = IntPtr.Zero; // 所有权已移交，避免重复删除

                // CF_DIB：BITMAPINFO + 像素数据（多数应用优先读取）
                var dib = BuildDibBlob(capture.Width, capture.Height, capture.BitsPtr);
                var dibHandle = CopyToHGlobal(dib);
                if (dibHandle == IntPtr.Zero)
                    return bitmapOwnedByClipboard;

                if (SetClipboardData(CF_Dib, dibHandle) == IntPtr.Zero)
                {
                    GlobalFree(dibHandle);
                    return bitmapOwnedByClipboard;
                }

                return true;
            }
            catch
            {
                return bitmapOwnedByClipboard;
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>根据任意位图（Lock 出的 BGRA 像素）构造一个 32 位 DIB 节点。</summary>
    private static ScreenCapture? CreateCaptureFromBitmap(WriteableBitmap bitmap)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;
        if (width <= 0 || height <= 0)
            return null;

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            return null;

        var bmi = new BitmapInfoHeader
        {
            biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BiRgb
        };

        var hBitmap = CreateDIBSection(screenDc, ref bmi, DibRgbColors, out var bitsPtr, IntPtr.Zero, 0);
        ReleaseDC(IntPtr.Zero, screenDc);
        if (hBitmap == IntPtr.Zero || bitsPtr == IntPtr.Zero)
        {
            DeleteObject(hBitmap);
            return null;
        }

        try
        {
            var rowBytes = width * 4;
            var buffer = new byte[rowBytes];
            using var framebuffer = bitmap.Lock();
            var srcRowBytes = framebuffer.RowBytes;

            for (var row = 0; row < height; row++)
            {
                Marshal.Copy(framebuffer.Address + row * srcRowBytes, buffer, 0, rowBytes);
                Marshal.Copy(buffer, 0, bitsPtr + row * rowBytes, rowBytes);
            }

            return new ScreenCapture(hBitmap, bitsPtr, width, height, rowBytes);
        }
        catch
        {
            DeleteObject(hBitmap);
            return null;
        }
    }

    /// <summary>把屏幕内容 BitBlt 到 32 位 DIB 节中（自顶向下，BGRA）。</summary>
    private static ScreenCapture? CaptureToDib()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var x = GetSystemMetrics(SmXvirtualscreen);
        var y = GetSystemMetrics(SmYvirtualscreen);
        var width = GetSystemMetrics(SmCxvirtualscreen);
        var height = GetSystemMetrics(SmCyvirtualscreen);

        if (width <= 0 || height <= 0)
            return null;

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            return null;

        var memDc = CreateCompatibleDC(screenDc);
        if (memDc == IntPtr.Zero)
        {
            ReleaseDC(IntPtr.Zero, screenDc);
            return null;
        }

        var bmi = new BitmapInfoHeader
        {
            biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            biWidth = width,
            biHeight = -height, // 负数表示自顶向下
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BiRgb
        };

        var hBitmap = CreateDIBSection(screenDc, ref bmi, DibRgbColors, out var bitsPtr, IntPtr.Zero, 0);
        if (hBitmap == IntPtr.Zero || bitsPtr == IntPtr.Zero)
        {
            DeleteObject(hBitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
            return null;
        }

        try
        {
            var oldObj = SelectObject(memDc, hBitmap);
            try
            {
                if (!BitBlt(memDc, 0, 0, width, height, screenDc, x, y, SrcCopy))
                {
                    DeleteObject(hBitmap);
                    return null;
                }
            }
            finally
            {
                SelectObject(memDc, oldObj);
            }

            return new ScreenCapture(hBitmap, bitsPtr, width, height, width * 4);
        }
        finally
        {
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>构造 CF_DIB 数据：BITMAPINFO（自底向上）+ 像素。</summary>
    private static byte[] BuildDibBlob(int width, int height, IntPtr topDownBits)
    {
        var headerSize = Marshal.SizeOf<BitmapInfoHeader>();
        var stride = width * 4;
        var blob = new byte[headerSize + stride * height];

        var header = new BitmapInfoHeader
        {
            biSize = (uint)headerSize,
            biWidth = width,
            biHeight = height, // 正数：自底向上，兼容性最好
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BiRgb
        };

        var headerPtr = Marshal.AllocHGlobal(headerSize);
        try
        {
            Marshal.StructureToPtr(header, headerPtr, false);
            Marshal.Copy(headerPtr, blob, 0, headerSize);
        }
        finally
        {
            Marshal.FreeHGlobal(headerPtr);
        }

        for (var row = 0; row < height; row++)
        {
            var srcRow = height - 1 - row;
            Marshal.Copy(topDownBits + srcRow * stride, blob, headerSize + row * stride, stride);
        }

        return blob;
    }

    private static IntPtr CopyToHGlobal(byte[] data)
    {
        var handle = GlobalAlloc(GmemMoveable | GmemZeroinit, (UIntPtr)(uint)data.Length);
        if (handle == IntPtr.Zero)
            return IntPtr.Zero;

        var ptr = GlobalLock(handle);
        try
        {
            Marshal.Copy(data, 0, ptr, data.Length);
        }
        finally
        {
            GlobalUnlock(handle);
        }

        return handle;
    }

    private sealed class ScreenCapture : IDisposable
    {
        public IntPtr HBitmap;
        public readonly IntPtr BitsPtr;
        public readonly int Width;
        public readonly int Height;
        public readonly int Stride;

        public ScreenCapture(IntPtr hBitmap, IntPtr bitsPtr, int width, int height, int stride)
        {
            HBitmap = hBitmap;
            BitsPtr = bitsPtr;
            Width = width;
            Height = height;
            Stride = stride;
        }

        public void Dispose()
        {
            if (HBitmap != IntPtr.Zero)
            {
                DeleteObject(HBitmap);
                HBitmap = IntPtr.Zero;
            }
        }
    }

    #region Windows API

    private const int SmXvirtualscreen = 76;
    private const int SmYvirtualscreen = 77;
    private const int SmCxvirtualscreen = 78;
    private const int SmCyvirtualscreen = 79;
    private const uint DibRgbColors = 0;
    private const uint BiRgb = 0;
    private const uint SrcCopy = 0x00CC0020;
    private const uint CF_Bitmap = 2;
    private const uint CF_Dib = 8;
    private const uint GmemMoveable = 0x0002;
    private const uint GmemZeroinit = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("user32.dll")]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader pbmi, uint usage,
        out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height,
        IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    #endregion
}
