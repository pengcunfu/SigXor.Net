using System;

namespace SigXor;

public sealed class NoOpKeyboardHook : IKeyboardHookService
{
    public int HoldThresholdMs { get; set; } = 400;
    public bool ScreenshotEnabled { get; set; } = true;
    public bool EscapeCaptureEnabled { get; set; }
    public bool IsSupported => false;

    event EventHandler? IKeyboardHookService.ShortcutPressed { add { } remove { } }
    event EventHandler? IKeyboardHookService.ShortcutReleased { add { } remove { } }
    event EventHandler? IKeyboardHookService.ShortcutHoldDetected { add { } remove { } }
    event EventHandler? IKeyboardHookService.ScreenshotShortcutPressed { add { } remove { } }
    event EventHandler? IKeyboardHookService.EscapePressed { add { } remove { } }

    public void Start() { }
    public void Stop() { }
    public void Dispose() { }
}
