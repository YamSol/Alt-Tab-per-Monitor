// Usage: WindowFarm <count> <offset>
// Opens <count> windows, prints one HWND per line (decimal), and exits when stdin closes.

namespace WindowFarm;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var count = int.Parse(args[0]);
        var offset = int.Parse(args[1]);

        for (var i = 0; i < count; i++)
        {
            var form = new Form
            {
                Text = $"Farm window {offset + i}",
                StartPosition = FormStartPosition.Manual,
                Location = new Point(120 + (offset + i) * 5, 120 + (offset + i) * 5),
                Size = new Size(320, 200),
            };
            form.Show();
            Console.WriteLine(form.Handle);
        }

        Console.Out.Flush();

        var context = SynchronizationContext.Current!;
        new Thread(() =>
        {
            Console.In.ReadToEnd();
            context.Post(_ => Application.Exit(), null);
        })
        {
            IsBackground = true,
        }.Start();

        Application.Run();
    }
}
