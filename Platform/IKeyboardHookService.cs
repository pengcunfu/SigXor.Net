using System;

namespace SigXor;

public interface IKeyboardHookService : IDisposable
{
    int HoldThresholdMs { get; set; }
    bool ScreenshotEnabled { get; set; }
    bool EscapeCaptureEnabled { get; set; }
    event EventHandler? ShortcutPressed;
    event EventHandler? ShortcutReleased;
    event EventHandler? ShortcutHoldDetected;
    event EventHandler? ScreenshotShortcutPressed;
    event EventHandler? EscapePressed;
    void Start();
    void Stop();
    bool IsSupported { get; }
}
