using MonitorSwitch.Core;

namespace MonitorSwitch.Win32.Tests;

public class HookTests
{
    [Fact]
    public void Keyboard_hook_installs_and_uninstalls()
    {
        // RF2. Installing needs no admin rights.
        using var hook = new KeyboardHook(_ => false, _ => { });

        hook.Start();
        Assert.True(hook.IsInstalled);

        hook.Dispose();
        Assert.False(hook.IsInstalled);
    }

    [Fact]
    public void Start_twice_is_harmless()
    {
        using var hook = new KeyboardHook(_ => false, _ => { });

        hook.Start();
        hook.Start();

        Assert.True(hook.IsInstalled);
    }

    [Fact]
    public void Emergency_hotkey_registers_and_unregisters()
    {
        // RF7. An unusual combination so the test does not collide with the user's own hotkeys.
        Assert.True(Hotkey.TryParse("Ctrl+Alt+Shift+Win+F23", out var hotkey));
        using var form = new Form();

        Assert.True(GlobalHotkey.Register(form.Handle, 0x4D53, hotkey!));
        Assert.False(GlobalHotkey.Register(form.Handle, 0x4D54, hotkey!)); // already taken
        GlobalHotkey.Unregister(form.Handle, 0x4D53);
        Assert.True(GlobalHotkey.Register(form.Handle, 0x4D53, hotkey!));
        GlobalHotkey.Unregister(form.Handle, 0x4D53);
    }

    [Fact]
    public void Foreground_watcher_hooks_and_unhooks()
    {
        using var watcher = new ForegroundWatcher(_ => { });
        watcher.Dispose();
    }

    [Fact]
    public void Alt_is_not_reported_as_held_while_idle()
    {
        Assert.False(KeyInjector.IsAltHeld());
    }

    [Fact]
    public void Virtual_desktop_manager_is_optional()
    {
        using var desktops = new VirtualDesktops();

        // Any top-level answer is acceptable; it must not throw and must default to "current".
        Assert.True(desktops.IsOnCurrentDesktop(0) || desktops.IsAvailable);
    }
}
