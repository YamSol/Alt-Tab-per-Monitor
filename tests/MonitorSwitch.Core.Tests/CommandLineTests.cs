namespace MonitorSwitch.Core.Tests;

public class CommandLineTests
{
    [Fact]
    public void No_arguments_runs_the_tray()
    {
        Assert.Equal(new CommandLine(Command.Tray), CommandLine.Parse([]));
    }

    [Theory]
    [InlineData("--list", Command.List)]
    [InlineData("--LIST-ALL", Command.ListAll)]
    [InlineData("--restore", Command.Restore)]
    [InlineData("--help", Command.Help)]
    [InlineData("/?", Command.Help)]
    public void Commands_parse(string arg, Command expected)
    {
        Assert.Equal(expected, CommandLine.Parse([arg]).Command);
    }

    [Fact]
    public void Options_combine_with_a_command_in_any_order()
    {
        var cli = CommandLine.Parse(["--data-dir", @"C:\portable", "--list", "--verbose"]);

        Assert.Equal(new CommandLine(Command.List, Verbose: true, DataDirectory: @"C:\portable"), cli);
    }

    [Theory]
    [InlineData("Unknown argument: --bogus", "--bogus")]
    [InlineData("--data-dir needs a folder.", "--data-dir")]
    [InlineData("Only one command at a time (--restore).", "--list", "--restore")]
    public void Bad_arguments_report_an_error(string error, params string[] args)
    {
        var cli = CommandLine.Parse(args);

        Assert.Equal(error, cli.Error);
        Assert.Equal(Command.Help, cli.Command);
    }

    [Fact]
    public void Usage_documents_every_option()
    {
        foreach (var option in new[] { "--list", "--list-all", "--restore", "--help", "--verbose", "--data-dir" })
        {
            Assert.Contains(option, CommandLine.Usage);
        }
    }
}
