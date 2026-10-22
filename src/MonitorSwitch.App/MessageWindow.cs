using MonitorSwitch.Core;
using MonitorSwitch.Win32;

namespace MonitorSwitch.App;

/// <summary>
/// Invisible top-level window that receives the emergency hotkey and <c>WM_QUERYENDSESSION</c>
/// (a message-only window would not get the session broadcast).
/// </summary>
internal sealed class MessageWindow : NativeWindow, IDisposable
{
    private const int EmergencyHotkeyId = 1;
    private readonly Action _onHotkey;
    private readonly Action _onSessionEnding;
    private bool _hotkeyRegistered;

    public MessageWindow(Action onHotkey, Action onSessionEnding)
    {
        _onHotkey = onHotkey;
        _onSessionEnding = onSessionEnding;
        CreateHandle(new CreateParams { Caption = "MonitorSwitch" });
    }

    public bool RegisterEmergencyHotkey(Hotkey hotkey)
    {
        UnregisterEmergencyHotkey();
        _hotkeyRegistered = GlobalHotkey.Register(Handle, EmergencyHotkeyId, hotkey);
        return _hotkeyRegistered;
    }

    public void Dispose()
    {
        UnregisterEmergencyHotkey();
        DestroyHandle();
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case GlobalHotkey.Message when m.WParam == EmergencyHotkeyId:
                _onHotkey();
                return;
            case GlobalHotkey.QueryEndSession:
                _onSessionEnding();
                m.Result = 1; // allow logoff
                return;
            case GlobalHotkey.EndSession:
                _onSessionEnding();
                return;
        }

        base.WndProc(ref m);
    }

    private void UnregisterEmergencyHotkey()
    {
        if (_hotkeyRegistered)
        {
            GlobalHotkey.Unregister(Handle, EmergencyHotkeyId);
            _hotkeyRegistered = false;
        }
    }
}
