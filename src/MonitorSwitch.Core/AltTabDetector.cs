namespace MonitorSwitch.Core;

/// <summary>A key transition as seen by the low-level hook. Only virtual-key codes, never characters.</summary>
/// <param name="AltDown"><c>LLKHF_ALTDOWN</c>: the system's own view of whether Alt is held.</param>
public readonly record struct KeyEvent(int VirtualKey, bool IsDown, bool AltDown = false);

public enum KeyAction
{
    None,

    /// <summary>Alt+Tab or Alt+Shift+Tab started: hide now.</summary>
    Begin,

    /// <summary>Alt released after a switch: restore and focus the chosen window.</summary>
    Commit,

    /// <summary>Esc while the switcher is open: restore without changing focus.</summary>
    Cancel,
}

/// <param name="Swallow">The hook must not pass this key on; the app re-injects it after hiding.</param>
/// <param name="Reverse">Shift was held when the switch began (Alt+Shift+Tab).</param>
public readonly record struct KeyDecision(KeyAction Action, bool Swallow = false, bool Reverse = false);

/// <summary>
/// Turns the raw key stream into Alt+Tab begin/commit/cancel (RF2). Keeps no history beyond
/// modifier state and records nothing about other keys.
/// </summary>
public sealed class AltTabDetector
{
    public const int VkTab = 0x09;
    public const int VkShift = 0x10;
    public const int VkControl = 0x11;
    public const int VkMenu = 0x12;
    public const int VkEscape = 0x1B;
    public const int VkLShift = 0xA0;
    public const int VkRShift = 0xA1;
    public const int VkLControl = 0xA2;
    public const int VkRControl = 0xA3;
    public const int VkLMenu = 0xA4;
    public const int VkRMenu = 0xA5;

    private enum State
    {
        Idle,
        AltHeld,
        Switching,
        Cancelled,
    }

    private State _state = State.Idle;
    private bool _shift;
    private bool _control;
    private bool _swallowTabUp;

    public AltTabDetector(bool deferFirstTab)
    {
        DeferFirstTab = deferFirstTab;
    }

    public bool DeferFirstTab { get; set; }

    public bool IsSwitching => _state == State.Switching;

    public KeyDecision Process(KeyEvent key)
    {
        switch (key.VirtualKey)
        {
            case VkShift or VkLShift or VkRShift:
                _shift = key.IsDown;
                return default;

            case VkControl or VkLControl or VkRControl:
                _control = key.IsDown;
                return default;

            case VkMenu or VkLMenu or VkRMenu:
                return OnAlt(key.IsDown);

            case VkTab:
                return OnTab(key);

            case VkEscape when key.IsDown && _state == State.Switching:
                // Esc is passed through: the system needs it to close its switcher.
                _state = State.Cancelled;
                return new KeyDecision(KeyAction.Cancel);

            default:
                return default;
        }
    }

    /// <summary>Forget everything, e.g. after the hook was reinstalled.</summary>
    public void Reset()
    {
        _state = State.Idle;
        _shift = _control = _swallowTabUp = false;
    }

    private KeyDecision OnAlt(bool isDown)
    {
        if (isDown)
        {
            if (_state == State.Idle)
            {
                _state = State.AltHeld;
            }

            return default;
        }

        var wasSwitching = _state == State.Switching;
        _state = State.Idle;
        return wasSwitching ? new KeyDecision(KeyAction.Commit) : default;
    }

    private KeyDecision OnTab(KeyEvent key)
    {
        if (!key.IsDown)
        {
            if (_swallowTabUp)
            {
                _swallowTabUp = false;
                return new KeyDecision(KeyAction.None, Swallow: true);
            }

            return default;
        }

        if (_state == State.Idle && key.AltDown)
        {
            // We missed the Alt-down (e.g. pressed on the secure desktop); trust the system flag.
            _state = State.AltHeld;
        }

        // Ctrl+Alt+Tab opens a sticky switcher that stays after Alt is released: out of scope.
        if (_state is State.AltHeld or State.Cancelled && !_control)
        {
            _state = State.Switching;
            _swallowTabUp = DeferFirstTab;
            return new KeyDecision(KeyAction.Begin, Swallow: DeferFirstTab, Reverse: _shift);
        }

        return default;
    }
}
