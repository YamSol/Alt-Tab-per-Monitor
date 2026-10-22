using System.Diagnostics;
using MonitorSwitch.Core;

namespace MonitorSwitch.Win32.Tests;

/// <summary>
/// Windows owned by separate <c>WindowFarm.exe</c> processes. Needed for timing: for windows of
/// the calling process, <c>GetWindowText</c> sends <c>WM_GETTEXT</c> to the owning thread, so
/// in-process test windows would measure the test's own UI threads instead of the tool.
/// </summary>
internal sealed class WindowFarmProcesses : IDisposable
{
    private readonly List<Process> _processes = [];

    public WindowFarmProcesses(int processes, int windowsEach)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "WindowFarm.exe");
        var handles = new List<WindowHandle>();
        for (var p = 0; p < processes; p++)
        {
            var process = Process.Start(new ProcessStartInfo(exe, $"{windowsEach} {p * windowsEach}")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            _processes.Add(process);
            for (var i = 0; i < windowsEach; i++)
            {
                handles.Add(new WindowHandle(nint.Parse(process.StandardOutput.ReadLine()!)));
            }
        }

        Handles = handles;
    }

    public IReadOnlyList<WindowHandle> Handles { get; }

    public void Dispose()
    {
        foreach (var process in _processes)
        {
            process.StandardInput.Close();
            if (!process.WaitForExit(5000))
            {
                process.Kill();
            }

            process.Dispose();
        }
    }
}
