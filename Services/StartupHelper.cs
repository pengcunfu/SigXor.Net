using System;
using System.IO;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace SigXor;

public static class StartupHelper
{
    public static bool IsSupported =>
        OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    public static bool IsEnabled()
    {
        if (OperatingSystem.IsWindows())
            return WindowsStartupHelper.IsEnabled();
        if (OperatingSystem.IsLinux())
            return LinuxStartupHelper.IsEnabled();
        if (OperatingSystem.IsMacOS())
            return MacStartupHelper.IsEnabled();
        return false;
    }

    public static void SetEnabled(bool enabled, bool silentOnAutoStart = false)
    {
        if (OperatingSystem.IsWindows())
            WindowsStartupHelper.SetEnabled(enabled, silentOnAutoStart);
        else if (OperatingSystem.IsLinux())
            LinuxStartupHelper.SetEnabled(enabled, silentOnAutoStart);
        else if (OperatingSystem.IsMacOS())
            MacStartupHelper.SetEnabled(enabled, silentOnAutoStart);
    }
}

internal static class WindowsStartupHelper
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "SigXor";

    [SupportedOSPlatform("windows")]
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(AppName) is string;
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    public static void SetEnabled(bool enabled, bool silentOnAutoStart)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);

        if (enabled)
        {
            var exePath = Environment.ProcessPath
                ?? Path.Combine(AppContext.BaseDirectory, "SigXor");
            var command = silentOnAutoStart ? $"\"{exePath}\" --silent" : $"\"{exePath}\"";
            key.SetValue(AppName, command);
        }
        else
        {
            key.DeleteValue(AppName, false);
        }
    }
}

internal static class LinuxStartupHelper
{
    private static string DesktopFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "autostart", "sigxor.desktop");

    public static bool IsEnabled() => File.Exists(DesktopFilePath);

    public static void SetEnabled(bool enabled, bool silentOnAutoStart)
    {
        if (enabled)
        {
            var exePath = Environment.ProcessPath
                ?? Path.Combine(AppContext.BaseDirectory, "SigXor");
            var args = silentOnAutoStart ? " --silent" : "";
            var dir = Path.GetDirectoryName(DesktopFilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(DesktopFilePath,
                $"""
                 [Desktop Entry]
                 Type=Application
                 Name=SigXor
                 Exec="{exePath}"{args}
                 X-GNOME-Autostart-enabled=true
                 """);
        }
        else if (File.Exists(DesktopFilePath))
        {
            File.Delete(DesktopFilePath);
        }
    }
}

internal static class MacStartupHelper
{
    private static string PlistPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "LaunchAgents", "com.sigxor.plist");

    public static bool IsEnabled() => File.Exists(PlistPath);

    public static void SetEnabled(bool enabled, bool silentOnAutoStart)
    {
        if (enabled)
        {
            var exePath = Environment.ProcessPath
                ?? Path.Combine(AppContext.BaseDirectory, "SigXor");
            var argsLine = silentOnAutoStart ? "    <string>--silent</string>\n" : "";
            var dir = Path.GetDirectoryName(PlistPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(PlistPath,
                $"""
                 <?xml version="1.0" encoding="UTF-8"?>
                 <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                 <plist version="1.0">
                 <dict>
                   <key>Label</key><string>com.sigxor</string>
                   <key>ProgramArguments</key>
                   <array>
                     <string>{exePath}</string>
                 {argsLine}  </array>
                   <key>RunAtLoad</key><true/>
                 </dict>
                 </plist>
                 """);
        }
        else if (File.Exists(PlistPath))
        {
            File.Delete(PlistPath);
        }
    }
}
