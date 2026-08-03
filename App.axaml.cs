using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace SigXor;

public partial class App : Application
{
    public static bool LaunchSilent { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            LaunchSilent = desktop.Args?.Contains("--silent") == true;

            var mainWindow = new MainWindow();
            desktop.MainWindow = mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            if (LaunchSilent)
                mainWindow.PrepareSilentStartup();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
