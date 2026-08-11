using System;

namespace SigXor;

public interface IKeyboardHookService : IDisposable
{
    int HoldThresholdMs { get; set; }
    string VoiceShortcut { get; set; }
    bool ScreenshotEnabled { get; set; }
    string ScreenshotModifier { get; set; }
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
