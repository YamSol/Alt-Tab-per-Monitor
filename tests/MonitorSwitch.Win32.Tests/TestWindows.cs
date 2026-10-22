using System.Diagnostics;
using MonitorSwitch.Core;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace MonitorSwitch.Win32.Tests;

/// <summary>
/// Owns STA threads with message loops and creates real forms on them, so the system processes
/// <c>ShowWindowAsync</c> and friends exactly as it would for another app's windows.
/// With several threads, windows are spread round-robin, like windows of different apps.
/// </summary>
internal sealed class TestWindows : IDisposable
{
    private readonly List<UiThread> _threads;
    private readonly VirtualDesktops _desktops = new();
    private int _next;

    public TestWindows(int threads = 1)
    {
        _threads = Enumerable.Range(0, threads).Select(_ => new UiThread()).ToList();
    }

    public IReadOnlyList<WindowHandle> All => _threads.SelectMany(t => t.Handles).ToList();

    public WindowHandle Create(
        string title,
        FormWindowState state = FormWindowState.Normal,
        bool visible = true,
        WindowHandle? owner = null,
        bool toolWindow = false,
        int offset = 0)
    {
        // Owned forms must live on the owner's thread.
        var thread = owner is { } o ? _threads.Single(t => t.Handles.Contains(o)) : _threads[_next++ % _threads.Count];
        var handle = thread.Create(title, state, visible, owner, toolWindow, offset);

        // The shell assigns a new window to a virtual desktop shortly after it is shown; until
        // then IsWindowOnCurrentVirtualDesktop says false. Wait so tests see a settled window.
        if (visible)
        {
            WaitFor(() => _desktops.IsOnCurrentDesktop(handle.Value));
        }

        return handle;
    }

    public void SetEnabled(WindowHandle window, bool enabled) =>
        _threads.Single(t => t.Handles.Contains(window)).SetEnabled(window, enabled);
    public static bool WaitFor(Func<bool> condition, int timeoutMs = 2000)
    {
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
    }

    public void Dispose()
    {
        foreach (var thread in _threads)
        {
            thread.Dispose();
        }

        _desktops.Dispose();
    }

    private sealed class UiThread : IDisposable
    {
        private readonly Thread _thread;
        private readonly List<Form> _forms = [];
        private Form _host = null!;

        public UiThread()
        {
            using var ready = new ManualResetEventSlim();
            _thread = new Thread(() =>
            {
                _host = new Form { ShowInTaskbar = false, Opacity = 0 };
                _ = _host.Handle;
                ready.Set();
                Application.Run();
            })
            {
                IsBackground = true,
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            ready.Wait();
        }

        public IReadOnlyList<WindowHandle> Handles => Invoke(() => _forms.Select(f => new WindowHandle(f.Handle)).ToList());

        public WindowHandle Create(string title, FormWindowState state, bool visible, WindowHandle? owner, bool toolWindow, int offset) =>
            Invoke(() =>
            {
                var form = new Form
                {
                    Text = title,
                    StartPosition = FormStartPosition.Manual,
                    Location = new System.Drawing.Point(100 + offset, 100 + offset),
                    Size = new System.Drawing.Size(320, 200),
                    ShowInTaskbar = !toolWindow,
                    FormBorderStyle = toolWindow ? FormBorderStyle.SizableToolWindow : FormBorderStyle.Sizable,
                };
                if (owner is { } o)
                {
                    form.Owner = _forms.Single(f => f.Handle == o.Value);
                }

                _forms.Add(form);
                form.Show();
                form.WindowState = state;
                if (!visible)
                {
                    form.Hide();
                }

                return new WindowHandle(form.Handle);
            });

        public void SetEnabled(WindowHandle window, bool enabled) =>
            Invoke(() => _forms.Single(f => f.Handle == window.Value).Enabled = enabled);

        public void Dispose()
        {
            Invoke(() =>
            {
                foreach (var form in _forms)
                {
                    form.Dispose();
                }

                Application.ExitThread();
                return 0;
            });
            _thread.Join(TimeSpan.FromSeconds(5));
        }

        private T Invoke<T>(Func<T> func) => (T)_host.Invoke(func);
    }
}

/// <summary>
/// Real <see cref="Win32WindowSystem"/>, but only the test's own windows are visible to the
/// planner and the cursor is on a monitor that does not exist, so every test window counts as
/// "on another monitor". Nothing else on the machine is ever hidden.
/// </summary>
internal sealed class ScopedWindowSystem : IWindowSystem
{
    public static readonly MonitorHandle Nowhere = new(-1);

    private readonly IWindowSystem _inner;
    private readonly Func<IReadOnlyList<WindowHandle>> _scope;

    public ScopedWindowSystem(IWindowSystem inner, Func<IReadOnlyList<WindowHandle>> scope)
    {
        _inner = inner;
        _scope = scope;
    }

    public IReadOnlyList<WindowInfo> EnumerateWindows()
    {
        var scope = _scope().ToHashSet();
        return _inner.EnumerateWindows().Where(w => scope.Contains(w.Handle)).ToList();
    }

    public IReadOnlyList<MonitorInfo> GetMonitors() => _inner.GetMonitors();

    public MonitorHandle GetCursorMonitor() => Nowhere;

    public bool IsWindow(WindowHandle window) => _inner.IsWindow(window);

    public uint GetProcessId(WindowHandle window) => _inner.GetProcessId(window);

    public bool IsVisible(WindowHandle window) => _inner.IsVisible(window);

    public WindowPlacement? GetPlacement(WindowHandle window) => _inner.GetPlacement(window);

    public bool Hide(WindowHandle window) => _inner.Hide(window);

    public bool WaitUntilHidden(IReadOnlyCollection<WindowHandle> windows, TimeSpan timeout) => _inner.WaitUntilHidden(windows, timeout);

    public bool Show(WindowHandle window, WindowPlacement placement, WindowHandle insertAfter) => _inner.Show(window, placement, insertAfter);

    public WindowHandle GetForegroundWindow() => _inner.GetForegroundWindow();

    public bool Focus(WindowHandle window) => _inner.Focus(window);
}
