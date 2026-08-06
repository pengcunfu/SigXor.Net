using System;

namespace SigXor;

public interface IKeyboardHookService : IDisposable
{
    int HoldThresholdMs { get; set; }
    bool ScreenshotEnabled { get; set; }
    event EventHandler? ShortcutPressed;
    event EventHandler? ShortcutReleased;
    event EventHandler? ShortcutHoldDetected;
    event EventHandler? ScreenshotShortcutPressed;
    void Start();
    void Stop();
    bool IsSupported { get; }
}
