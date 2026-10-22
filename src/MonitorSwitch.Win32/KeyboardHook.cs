using System.Runtime.InteropServices;
using MonitorSwitch.Core;
using static MonitorSwitch.Win32.NativeMethods;

namespace MonitorSwitch.Win32;

/// <summary>
/// <c>WH_KEYBOARD_LL</c> on a dedicated thread with its own message loop (RF2). The callback must
/// return within <c>LowLevelHooksTimeout</c>, so it only classifies the key and hands work off;
/// hiding happens on the UI thread. Our own injected keys (tagged with <see cref="InjectionMarker"/>)
/// are passed through untouched.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    /// <summary><c>dwExtraInfo</c> on keys we inject, so the hook recognises and ignores them.</summary>
    public static readonly nint InjectionMarker = 0x4D53_5754; // "MSWT"

    private readonly Func<KeyEvent, bool> _onKey;
    private readonly Action<Exception> _onFailure;
    private readonly LowLevelKeyboardProc _callback;
    private Thread? _thread;
    private uint _threadId;
    private nint _hook;

    /// <param name="onKey">Called on the hook thread for every key; return true to swallow it.</param>
    /// <param name="onFailure">Called when the callback throws; the hook is already uninstalled by then.</param>
    public KeyboardHook(Func<KeyEvent, bool> onKey, Action<Exception> onFailure)
    {
        _onKey = onKey;
        _onFailure = onFailure;
        _callback = Callback; // keep the delegate alive for as long as the hook exists
    }

    public bool IsInstalled => _hook != 0;

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        using var ready = new ManualResetEventSlim();
        Exception? error = null;
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookExW(WH_KEYBOARD_LL, _callback, GetModuleHandleW(null), 0);
            if (_hook == 0)
            {
                error = new InvalidOperationException($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
            }

            ready.Set();
            if (_hook == 0)
            {
                return;
            }

            while (GetMessageW(out var msg, 0, 0, 0) > 0)
            {
                // Low-level hooks are dispatched inside GetMessage; nothing else to do.
            }

            Uninstall();
        })
        {
            IsBackground = true,
            Name = "MonitorSwitch keyboard hook",
        };
        _thread.Start();
        ready.Wait();

        if (error is not null)
        {
            _thread = null;
            throw error;
        }
    }

    public void Dispose()
    {
        if (_thread is null)
        {
            return;
        }

        PostThreadMessageW(_threadId, WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }

    private nint Callback(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0 || _hook == 0)
        {
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if ((data.flags & LLKHF_INJECTED) != 0 && data.dwExtraInfo == InjectionMarker)
        {
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        var message = (int)wParam;
        var isDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
        var key = new KeyEvent((int)data.vkCode, isDown, (data.flags & LLKHF_ALTDOWN) != 0);

        try
        {
            if (_onKey(key))
            {
                return 1;
            }
        }
        catch (Exception ex)
        {
            // Spec §5: if the hook fails it is removed, and the keyboard keeps working.
            Uninstall();
            PostThreadMessageW(_threadId, WM_QUIT, 0, 0);
            _onFailure(ex);
        }

        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    private void Uninstall()
    {
        var hook = Interlocked.Exchange(ref _hook, 0);
        if (hook != 0)
        {
            UnhookWindowsHookEx(hook);
        }
    }
}
