using System.Runtime.InteropServices;

namespace MonitorSwitch.Win32;

/// <summary>
/// Wraps the documented <c>IVirtualDesktopManager</c> (RF9). If the COM object is unavailable,
/// every window counts as being on the current desktop, which matches a machine without desktops.
/// </summary>
public sealed class VirtualDesktops : IDisposable
{
    private readonly IVirtualDesktopManager? _manager;

    public VirtualDesktops()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a"));
            _manager = type is null ? null : Activator.CreateInstance(type) as IVirtualDesktopManager;
        }
        catch (COMException)
        {
            _manager = null;
        }
    }

    public bool IsAvailable => _manager is not null;

    public bool IsOnCurrentDesktop(nint hwnd)
    {
        if (_manager is null)
        {
            return true;
        }

        try
        {
            return _manager.IsWindowOnCurrentVirtualDesktop(hwnd, out var onCurrent) != 0 || onCurrent;
        }
        catch (COMException)
        {
            return true;
        }
    }

    public void Dispose()
    {
        if (_manager is not null)
        {
            Marshal.ReleaseComObject(_manager);
        }
    }

    [ComImport]
    [Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        // Returns an HRESULT; a failure (e.g. windows of elevated apps) is treated as "on current".
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(nint topLevelWindow, [MarshalAs(UnmanagedType.Bool)] out bool onCurrentDesktop);

        [PreserveSig]
        int GetWindowDesktopId(nint topLevelWindow, out Guid desktopId);

        [PreserveSig]
        int MoveWindowToDesktop(nint topLevelWindow, ref Guid desktopId);
    }
}
