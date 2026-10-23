using System.Diagnostics;

namespace MonitorSwitch.Win32.Tests;

/// <summary>
/// Starts the real MonitorSwitch.exe. v0.1.0 passed every unit test and still crashed on start,
/// because nothing ever ran the tray; these tests do. Each run uses its own data folder (and so
/// its own instance mutex), so a MonitorSwitch the developer has running is not disturbed.
/// </summary>
public sealed class TrayStartupTests : IDisposable
{
    private static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "MonitorSwitch.exe");
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "monitorswitch-tray-" + Guid.NewGuid().ToString("N"));

    private string LogFile => Path.Combine(_dataDir, "monitorswitch.log");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dataDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Tray_starts_stays_up_and_logs_its_startup()
    {
        // Active is the default; a real Alt+Tab by the developer during the test must hide nothing.
        Directory.CreateDirectory(_dataDir);
        File.WriteAllText(Path.Combine(_dataDir, "settings.json"), """{ "Mode": "ViewOnly" }""");

        using var process = Process.Start(Exe, ["--verbose", "--data-dir", _dataDir]);
        try
        {
            Assert.True(TestWindows.WaitFor(() => ReadLog().Contains("INFO Ready in"), timeoutMs: 15000), $"Never became ready. Log:\n{ReadLog()}");
            Thread.Sleep(1000);

            Assert.False(process.HasExited, $"Exited with {(process.HasExited ? process.ExitCode : 0)}. Log:\n{ReadLog()}");
            var log = ReadLog();
            Assert.DoesNotContain("ERROR", log);
            Assert.Contains("Keyboard hook installed.", log);
            Assert.Contains("Mode=ViewOnly", log);
            Assert.Contains("DEBUG", log);
        }
        finally
        {
            process.Kill();
            process.WaitForExit(5000);
        }
    }

    [Theory]
    [InlineData("--help", 0)]
    [InlineData("--bogus", 2)]
    public void Command_line_exit_codes(string argument, int expected)
    {
        using var process = Process.Start(new ProcessStartInfo(Exe, [argument, "--data-dir", _dataDir])
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;

        Assert.True(process.WaitForExit(15000));
        Assert.Equal(expected, process.ExitCode);
    }

    private string ReadLog()
    {
        try
        {
            using var stream = new FileStream(LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return new StreamReader(stream).ReadToEnd();
        }
        catch (IOException)
        {
            return "";
        }
    }
}
