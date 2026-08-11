using Avalonia;
using System;

namespace SigXor;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (!SingleInstance.TryAcquire(args))
            return;

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            SingleInstance.Release();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
