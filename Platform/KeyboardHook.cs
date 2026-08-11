using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SigXor
{
    public class KeyboardHook : IKeyboardHookService
    {
        private const int WhKeyboardLl = 13;
        private const int WmKeydown = 0x0100;
        private const int WmKeyup = 0x0101;
        private const int WmSyskeydown = 0x0104;
        private const int WmSyskeyup = 0x0105;
        private const uint VkLMenu = 0xA4;
        private const uint VkRMenu = 0xA5;
        private const uint VkLControl = 0xA2;
        private const uint VkRControl = 0xA3;
        private const uint VkCapital = 0x14;
        private const uint VkOem3 = 0xC0; // ` ~ 键
        private const uint VkEscape = 0x1B;
        private const uint VkShift = 0x10;
        private const uint VkControl = 0x11;
        private const uint VkMenu = 0x12;
        private const uint VkLWin = 0x5B;
        private const uint VkRWin = 0x5C;
        private const uint LlkhfInjected = 0x10;

        private IntPtr _hookId = IntPtr.Zero;
        private readonly LowLevelKeyboardProc _proc;
        private bool _isHooked;
        private bool _isKeyDown;
        private bool _voiceSuppressed;
        private bool _screenshotKeyDown;
        private bool _screenshotSuppressed;
        private bool _escapeKeyDown;
        private bool _escapeSuppressed;
        private CancellationTokenSource? _holdCts;

        /// <summary>按住超过该时长视为「长按模式」，否则为「点击切换」</summary>
        public int HoldThresholdMs { get; set; } = 400;

        /// <summary>语音输入快捷键：right-alt / left-alt / right-ctrl / left-ctrl / caps-lock</summary>
        public string VoiceShortcut { get; set; } = "right-alt";

        /// <summary>是否启用截屏快捷键</summary>
        public bool ScreenshotEnabled { get; set; } = true;

        /// <summary>截屏修饰键：alt / ctrl / ctrl+shift / win</summary>
        public string ScreenshotModifier { get; set; } = "alt";

        /// <summary>截屏流程期间接管 ESC 键：吞掉按键并触发 EscapePressed</summary>
        public bool EscapeCaptureEnabled { get; set; }

        public bool IsSupported => true;

        public event EventHandler? ShortcutPressed;
        public event EventHandler? ShortcutReleased;
        public event EventHandler? ShortcutHoldDetected;
        public event EventHandler? ScreenshotShortcutPressed;
        public event EventHandler? EscapePressed;

        public KeyboardHook()
        {
            _proc = HookCallback;
        }

        public void Start()
        {
            if (_isHooked)
                return;

            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            {
                _hookId = SetWindowsHookEx(WhKeyboardLl, _proc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }

            _isHooked = _hookId != IntPtr.Zero;
        }

        public void Stop()
        {
            if (!_isHooked)
                return;

            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            _isHooked = false;
            _isKeyDown = false;
            _voiceSuppressed = false;
            _screenshotKeyDown = false;
            _screenshotSuppressed = false;
            _escapeKeyDown = false;
            _escapeSuppressed = false;
            CancelHoldTimer();
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && lParam != IntPtr.Zero)
            {
                var hookStruct = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                var isKeyDown = wParam == (IntPtr)WmKeydown || wParam == (IntPtr)WmSyskeydown;
                var isKeyUp = wParam == (IntPtr)WmKeyup || wParam == (IntPtr)WmSyskeyup;
                var isInjected = (hookStruct.flags & LlkhfInjected) != 0;
                var voiceVk = ResolveVoiceVk();

                if (!isInjected && hookStruct.vkCode == voiceVk)
                {
                    if (isKeyDown)
                    {
                        if (_voiceSuppressed)
                            return (IntPtr)1;

                        if (!_isKeyDown)
                        {
                            _isKeyDown = true;
                            StartHoldTimer();
                            ShortcutPressed?.Invoke(this, EventArgs.Empty);
                            if (ShouldSuppressVoiceKey())
                            {
                                _voiceSuppressed = true;
                                return (IntPtr)1;
                            }
                        }
                        else if (_voiceSuppressed)
                        {
                            return (IntPtr)1;
                        }
                    }
                    else if (isKeyUp)
                    {
                        var wasSuppressed = _voiceSuppressed;
                        if (_isKeyDown)
                        {
                            _isKeyDown = false;
                            CancelHoldTimer();
                            ShortcutReleased?.Invoke(this, EventArgs.Empty);
                        }

                        _voiceSuppressed = false;
                        if (wasSuppressed)
                            return (IntPtr)1;
                    }
                }

                if (!isInjected && hookStruct.vkCode == VkOem3)
                {
                    if (isKeyDown)
                    {
                        if (_screenshotSuppressed)
                            return (IntPtr)1;

                        if (!_screenshotKeyDown)
                        {
                            _screenshotKeyDown = true;
                            if (ScreenshotEnabled && MatchesScreenshotShortcut())
                            {
                                _screenshotSuppressed = true;
                                ScreenshotShortcutPressed?.Invoke(this, EventArgs.Empty);
                                return (IntPtr)1;
                            }
                        }
                    }
                    else if (isKeyUp)
                    {
                        var wasSuppressed = _screenshotSuppressed;
                        _screenshotKeyDown = false;
                        _screenshotSuppressed = false;
                        if (wasSuppressed)
                            return (IntPtr)1;
                    }
                }

                if (!isInjected && hookStruct.vkCode == VkEscape)
                {
                    if (isKeyDown)
                    {
                        if (_escapeSuppressed)
                            return (IntPtr)1;

                        if (!_escapeKeyDown)
                        {
                            _escapeKeyDown = true;
                            if (EscapeCaptureEnabled)
                            {
                                _escapeSuppressed = true;
                                EscapePressed?.Invoke(this, EventArgs.Empty);
                                return (IntPtr)1;
                            }
                        }
                    }
                    else if (isKeyUp)
                    {
                        var wasSuppressed = _escapeSuppressed;
                        _escapeKeyDown = false;
                        _escapeSuppressed = false;
                        if (wasSuppressed)
                            return (IntPtr)1;
                    }
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private uint ResolveVoiceVk() =>
            Config.NormalizeVoiceShortcut(VoiceShortcut) switch
            {
                "left-alt" => VkLMenu,
                "right-ctrl" => VkRControl,
                "left-ctrl" => VkLControl,
                "caps-lock" => VkCapital,
                _ => VkRMenu
            };

        private bool ShouldSuppressVoiceKey() =>
            Config.NormalizeVoiceShortcut(VoiceShortcut) == "caps-lock";

        private bool MatchesScreenshotShortcut()
        {
            var modifier = Config.NormalizeScreenshotShortcut(ScreenshotModifier);
            return modifier switch
            {
                "ctrl" => IsCtrlForScreenshot() && !IsKeyDown(VkMenu) && !IsKeyDown(VkShift) && !IsWinDown(),
                "ctrl+shift" => IsCtrlForScreenshot() && IsKeyDown(VkShift) && !IsKeyDown(VkMenu) && !IsWinDown(),
                "win" => IsWinDown() && !IsKeyDown(VkControl) && !IsKeyDown(VkMenu) && !IsKeyDown(VkShift),
                // 避开当前语音快捷键占用的那一侧 Alt
                _ => IsAltForScreenshot() && !IsKeyDown(VkControl) && !IsKeyDown(VkShift) && !IsWinDown()
            };
        }

        private bool IsAltForScreenshot()
        {
            // 语音占用左 Alt 时，截屏改认右 Alt，避免互相抢键
            return Config.NormalizeVoiceShortcut(VoiceShortcut) == "left-alt"
                ? IsKeyDown(VkRMenu)
                : IsKeyDown(VkLMenu);
        }

        private bool IsCtrlForScreenshot()
        {
            return Config.NormalizeVoiceShortcut(VoiceShortcut) switch
            {
                "left-ctrl" => IsKeyDown(VkRControl),
                "right-ctrl" => IsKeyDown(VkLControl),
                _ => IsKeyDown(VkControl)
            };
        }

        private static bool IsWinDown() =>
            IsKeyDown(VkLWin) || IsKeyDown(VkRWin);

        private static bool IsKeyDown(uint vk) =>
            (GetAsyncKeyState((int)vk) & 0x8000) != 0;

        private void StartHoldTimer()
        {
            CancelHoldTimer();
            _holdCts = new CancellationTokenSource();
            var token = _holdCts.Token;

            Task.Delay(HoldThresholdMs, token).ContinueWith(task =>
            {
                if (!task.IsCanceled && _isKeyDown)
                    ShortcutHoldDetected?.Invoke(this, EventArgs.Empty);
            }, TaskScheduler.Default);
        }

        private void CancelHoldTimer()
        {
            _holdCts?.Cancel();
            _holdCts?.Dispose();
            _holdCts = null;
        }

        public void Dispose()
        {
            Stop();
            CancelHoldTimer();
        }

        #region Windows API

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [StructLayout(LayoutKind.Sequential)]
        private struct KbdLlHookStruct
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        #endregion
    }
}
