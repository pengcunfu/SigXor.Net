using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Avalonia.Threading;

namespace SigXor;

/// <summary>
/// Ensures only one SigXor process runs. A later launch signals the first
/// instance to show its main window instead of opening another UI.
/// </summary>
internal static class SingleInstance
{
    private const string MutexName = @"Local\SigXor.SingleInstance.Mutex";
    private const string ActivateEventName = @"Local\SigXor.SingleInstance.Activate";

    private static Mutex? _mutex;
    private static EventWaitHandle? _activateEvent;
    private static CancellationTokenSource? _listenerCts;

    /// <returns>True if this process should continue as the sole instance.</returns>
    public static bool TryAcquire(string[] args)
    {
        if (!OperatingSystem.IsWindows())
            return true;

        return TryAcquireWindows(args);
    }

    [SupportedOSPlatform("windows")]
    private static bool TryAcquireWindows(string[] args)
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (createdNew)
        {
            _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            return true;
        }

        _mutex.Dispose();
        _mutex = null;

        // Autostart / silent relaunch should not pop the existing window.
        if (args.Contains("--silent", StringComparer.OrdinalIgnoreCase))
            return false;

        try
        {
            AllowExistingInstanceForeground();
            using var activate = EventWaitHandle.OpenExisting(ActivateEventName);
            activate.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // First instance is shutting down or not ready; just exit.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return false;
    }

    public static void StartActivateListener(Action onActivate)
    {
        if (_activateEvent == null)
            return;

        _listenerCts = new CancellationTokenSource();
        var token = _listenerCts.Token;
        var thread = new Thread(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_activateEvent.WaitOne(500))
                        Dispatcher.UIThread.Post(onActivate);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        })
        {
            IsBackground = true,
            Name = "SigXor.SingleInstance.ActivateListener"
        };
        thread.Start();
    }

    public static void Release()
    {
        _listenerCts?.Cancel();
        _listenerCts?.Dispose();
        _listenerCts = null;

        _activateEvent?.Dispose();
        _activateEvent = null;

        if (_mutex == null)
            return;

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        _mutex.Dispose();
        _mutex = null;
    }

    [SupportedOSPlatform("windows")]
    private static void AllowExistingInstanceForeground()
    {
        var currentId = Environment.ProcessId;
        foreach (var process in Process.GetProcessesByName("SigXor"))
        {
            try
            {
                if (process.Id == currentId)
                    continue;

                AllowSetForegroundWindow(process.Id);
                break;
            }
            catch
            {
                // Ignore processes we cannot query.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);
}
