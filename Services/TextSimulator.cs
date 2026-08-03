using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace SigXor;

public class TextSimulator
{
    private readonly double _typingDelay;
    private Window? _ownerWindow;

    public TextSimulator(double typingDelay = 0.05)
    {
        _typingDelay = typingDelay;
    }

    public void SetOwnerWindow(Window? window) => _ownerWindow = window;

    public void CaptureTargetWindow()
    {
        if (OperatingSystem.IsWindows())
            WindowsFocusHelper.CaptureTargetWindow();
    }

    public async Task TypeTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (OperatingSystem.IsWindows())
        {
            WindowsFocusHelper.RestoreTargetWindow();
            await Task.Delay(120);
            await WindowsTextInput.TypeAsync(text);
        }
        else if (OperatingSystem.IsLinux())
        {
            await Task.Delay(100);
            await LinuxTextInput.TypeAsync(text);
        }
        else if (OperatingSystem.IsMacOS())
        {
            await Task.Delay(100);
            await MacTextInput.TypeAsync(text);
        }
        else
            throw new PlatformNotSupportedException("当前平台不支持键盘模拟输入，请启用「使用剪贴板粘贴」。");
    }

    public async Task InsertTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        string? originalText = null;
        if (OperatingSystem.IsWindows())
            originalText = WindowsClipboardHelper.GetText();
        else
        {
            var clipboard = GetClipboard();
            if (clipboard == null)
                throw new InvalidOperationException("无法访问剪贴板");
            originalText = await clipboard.GetTextAsync();
        }

        if (OperatingSystem.IsWindows())
        {
            WindowsClipboardHelper.SetText(text);
            WindowsFocusHelper.RestoreTargetWindow();
            await Task.Delay(120);
            await WindowsTextInput.PasteAsync();
        }
        else
        {
            var clipboard = GetClipboard();
            if (clipboard == null)
                throw new InvalidOperationException("无法访问剪贴板");

            await clipboard.SetTextAsync(text);
            await Task.Delay(50);

            if (OperatingSystem.IsLinux())
            {
                await Task.Delay(100);
                await LinuxTextInput.PasteAsync();
            }
            else if (OperatingSystem.IsMacOS())
            {
                await Task.Delay(100);
                await MacTextInput.PasteAsync();
            }
            else
                throw new PlatformNotSupportedException("当前平台不支持粘贴模拟");
        }

        await Task.Delay(50);

        if (OperatingSystem.IsWindows())
        {
            if (!string.IsNullOrEmpty(originalText))
                WindowsClipboardHelper.SetText(originalText);
            else
                WindowsClipboardHelper.Clear();
        }
        else
        {
            var clipboard = GetClipboard();
            if (clipboard == null)
                return;

            if (!string.IsNullOrEmpty(originalText))
                await clipboard.SetTextAsync(originalText);
            else
                await clipboard.ClearAsync();
        }
    }

    private IClipboard? GetClipboard() =>
        _ownerWindow?.Clipboard ?? TopLevel.GetTopLevel(_ownerWindow)?.Clipboard;
}

internal static class WindowsFocusHelper
{
    private static nint _targetWindow;
    private static readonly uint OwnProcessId = (uint)Process.GetCurrentProcess().Id;

    public static void CaptureTargetWindow()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == nint.Zero)
            return;

        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == OwnProcessId)
            return;

        _targetWindow = hwnd;
    }

    public static void RestoreTargetWindow()
    {
        if (_targetWindow == nint.Zero)
            return;

        if (GetForegroundWindow() == _targetWindow)
        {
            FocusTargetControl();
            return;
        }

        var foreground = GetForegroundWindow();
        var foregroundThread = foreground != nint.Zero
            ? GetWindowThreadProcessId(foreground, out _)
            : 0u;
        var targetThread = GetWindowThreadProcessId(_targetWindow, out _);
        var currentThread = GetCurrentThreadId();

        if (foregroundThread != 0 && foregroundThread != targetThread)
            AttachThreadInput(foregroundThread, targetThread, true);

        if (currentThread != targetThread)
            AttachThreadInput(currentThread, targetThread, true);

        if (IsIconic(_targetWindow))
            ShowWindow(_targetWindow, SwRestore);

        BringWindowToTop(_targetWindow);
        SetForegroundWindow(_targetWindow);
        FocusTargetControl();

        if (currentThread != targetThread)
            AttachThreadInput(currentThread, targetThread, false);

        if (foregroundThread != 0 && foregroundThread != targetThread)
            AttachThreadInput(foregroundThread, targetThread, false);
    }

    private static void FocusTargetControl()
    {
        var targetThread = GetWindowThreadProcessId(_targetWindow, out _);
        var currentThread = GetCurrentThreadId();
        var attached = false;

        if (currentThread != targetThread)
            attached = AttachThreadInput(currentThread, targetThread, true);

        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        if (GetGUIThreadInfo(targetThread, ref info) && info.hwndFocus != nint.Zero)
            SetFocus(info.hwndFocus);

        if (attached)
            AttachThreadInput(currentThread, targetThread, false);
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }
}

internal static class WindowsClipboardHelper
{
    private const uint CfUnicode = 13;

    public static string? GetText()
    {
        if (!OpenClipboard(nint.Zero))
            return null;

        try
        {
            var handle = GetClipboardData(CfUnicode);
            if (handle == nint.Zero)
                return null;

            var pointer = GlobalLock(handle);
            if (pointer == nint.Zero)
                return null;

            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static void SetText(string text)
    {
        if (!OpenClipboard(nint.Zero))
            throw new InvalidOperationException("无法打开剪贴板");

        try
        {
            EmptyClipboard();
            var bytes = Encoding.Unicode.GetBytes(text + '\0');
            var hGlobal = GlobalAlloc(GmemMoveable, (nuint)bytes.Length);
            if (hGlobal == nint.Zero)
                throw new InvalidOperationException("无法分配剪贴板内存");

            var target = GlobalLock(hGlobal);
            if (target == nint.Zero)
                throw new InvalidOperationException("无法锁定剪贴板内存");

            try
            {
                Marshal.Copy(bytes, 0, target, bytes.Length);
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            if (SetClipboardData(CfUnicode, hGlobal) == nint.Zero)
                throw new InvalidOperationException("无法写入剪贴板");
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static void Clear()
    {
        if (!OpenClipboard(nint.Zero))
            return;

        try
        {
            EmptyClipboard();
        }
        finally
        {
            CloseClipboard();
        }
    }

    private const uint GmemMoveable = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(nint hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint uFormat, nint hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(nint hMem);
}

internal static class WindowsTextInput
{
    public static Task TypeAsync(string text)
    {
        foreach (var ch in text)
            SendChar(ch);

        return Task.CompletedTask;
    }

    public static Task PasteAsync()
    {
        SendKeyCombo(0x11, 0x56); // Ctrl+V
        return Task.CompletedTask;
    }

    private static void SendChar(char ch)
    {
        var inputs = new INPUT[2];
        inputs[0].type = InputKeyboard;
        inputs[0].U.ki = new KEYBDINPUT
        {
            wScan = ch,
            dwFlags = KeyeventfUnicode
        };

        inputs[1].type = InputKeyboard;
        inputs[1].U.ki = new KEYBDINPUT
        {
            wScan = ch,
            dwFlags = KeyeventfUnicode | KeyeventfKeyup
        };

        SendInput(2, inputs, InputSize);
    }

    private static void SendKeyCombo(ushort modifier, ushort key)
    {
        var inputs = new INPUT[4];
        inputs[0] = KeyDown(modifier);
        inputs[1] = KeyDown(key);
        inputs[2] = KeyUp(key);
        inputs[3] = KeyUp(modifier);
        SendInput(4, inputs, InputSize);
    }

    private static INPUT KeyDown(ushort vk) => new()
    {
        type = InputKeyboard,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk } }
    };

    private static INPUT KeyUp(ushort vk) => new()
    {
        type = InputKeyboard,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KeyeventfKeyup } }
    };

    private const uint InputKeyboard = 1;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfUnicode = 0x0004;
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }
}

internal static class LinuxTextInput
{
    public static Task TypeAsync(string text) =>
        RunAsync("xdotool", $"type --delay 1 -- {EscapeArg(text)}");

    public static Task PasteAsync() =>
        RunAsync("xdotool", "key ctrl+v");

    private static string EscapeArg(string text) =>
        "'" + text.Replace("'", "'\\''") + "'";

    private static Task RunAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 xdotool，请安装: sudo apt install xdotool");
        process.WaitForExit();
        return Task.CompletedTask;
    }
}

internal static class MacTextInput
{
    public static Task TypeAsync(string text)
    {
        var escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return RunAsync("osascript", $"-e \"tell application \\\"System Events\\\" to keystroke \\\"{escaped}\\\"\"");
    }

    public static Task PasteAsync() =>
        RunAsync("osascript", "-e 'tell application \"System Events\" to keystroke \"v\" using command down'");

    private static Task RunAsync(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("无法执行 osascript");
        process.WaitForExit();
        return Task.CompletedTask;
    }
}
