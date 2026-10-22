namespace MonitorSwitch.Win32;

public static class ConsoleAttach
{
    private const int AttachParentProcess = -1;

    /// <summary>Lets the WinExe print to the terminal it was started from (for <c>--list</c>).</summary>
    public static bool ToParent() => NativeMethods.AttachConsole(AttachParentProcess);
}
