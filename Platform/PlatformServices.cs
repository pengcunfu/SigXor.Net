namespace SigXor;

public static class PlatformServices
{
    public static IKeyboardHookService CreateKeyboardHook() =>
        OperatingSystem.IsWindows() ? new KeyboardHook() : new NoOpKeyboardHook();
}
