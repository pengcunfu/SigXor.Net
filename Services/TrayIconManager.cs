using System;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SigXor;

public class TrayIconManager : IDisposable
{
    private readonly TrayIcon _trayIcon;

    public event EventHandler? ShowWindowRequested;
    public event EventHandler? ExitRequested;

    public TrayIconManager()
    {
        var menu = new NativeMenu
        {
            Items =
            {
                CreateMenuItem("显示主窗口", (_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty)),
                CreateMenuItem("退出", (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty))
            }
        };

        _trayIcon = new TrayIcon
        {
            ToolTipText = "SigXor",
            Icon = LoadTrayIcon(),
            Menu = menu,
            IsVisible = true
        };

        _trayIcon.Clicked += (_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    private static NativeMenuItem CreateMenuItem(string header, EventHandler click)
    {
        var item = new NativeMenuItem { Header = header };
        item.Click += click;
        return item;
    }

    public void SetServiceRunning(bool running)
    {
        _trayIcon.ToolTipText = running ? "SigXor - 服务运行中" : "SigXor - 服务已停止";
    }

    public void ShowBalloon(string title, string message)
    {
        _trayIcon.ToolTipText = $"{title}: {message}";
    }

    private static WindowIcon LoadTrayIcon()
    {
        var assets = AssetLoader.Open(new Uri("avares://SigXor/icon.png"));
        return new WindowIcon(new Bitmap(assets));
    }

    public void Dispose()
    {
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
    }
}
