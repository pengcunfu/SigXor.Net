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
        private const int VkRMenu = 0xA5; // 右 Alt
        private const uint VkOem3 = 0xC0; // ` ~ 键（笔记本 Fn 对系统不可见，实际以该键触发）
        private const uint VkEscape = 0x1B;
        private const uint VkShift = 0x10;
        private const uint VkControl = 0x11;
        private const uint VkMenu = 0x12;
        private const uint VkLWin = 0x5B;
        private const uint VkRWin = 0x5C;
        private const uint LlkhfInjected = 0x10; // 注入事件标志，避免吞掉程序自己输入的反引号

        private IntPtr _hookId = IntPtr.Zero;
        private readonly LowLevelKeyboardProc _proc;
        private bool _isHooked;
        private bool _isKeyDown;
        private bool _screenshotKeyDown;
        private bool _screenshotSuppressed;
        private bool _escapeKeyDown;
        private bool _escapeSuppressed;
        private CancellationTokenSource? _holdCts;

        /// <summary>按住超过该时长视为「长按模式」，否则为「点击切换」</summary>
        public int HoldThresholdMs { get; set; } = 400;

        /// <summary>是否启用 Fn + ` 截屏快捷键</summary>
        public bool ScreenshotEnabled { get; set; } = true;

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

                if (hookStruct.vkCode == VkRMenu)
                {
                    if (isKeyDown && !_isKeyDown)
                    {
                        _isKeyDown = true;
                        StartHoldTimer();
                        ShortcutPressed?.Invoke(this, EventArgs.Empty);
                    }
                    else if (isKeyUp && _isKeyDown)
                    {
                        _isKeyDown = false;
                        CancelHoldTimer();
                        ShortcutReleased?.Invoke(this, EventArgs.Empty);
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
                            if (ScreenshotEnabled && !IsAnyModifierDown())
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

        private static bool IsAnyModifierDown() =>
            IsKeyDown(VkShift) || IsKeyDown(VkControl) || IsKeyDown(VkMenu)
            || IsKeyDown(VkLWin) || IsKeyDown(VkRWin);

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
