using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace SigXor;

/// <summary>
/// 截图预览窗口：固定在选区位置显示裁剪出的图像（1:1 物理像素），
/// 让用户在使用工具条时能直接看到截屏内容。
/// </summary>
public partial class RegionPreviewWindow : Window
{
    private PixelRect _screenRect;

    public RegionPreviewWindow()
    {
        InitializeComponent();
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
}
