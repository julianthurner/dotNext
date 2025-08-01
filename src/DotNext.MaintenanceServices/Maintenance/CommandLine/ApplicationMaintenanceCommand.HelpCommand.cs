using System.CommandLine;
using System.CommandLine.Help;

namespace DotNext.Maintenance.CommandLine;

partial class ApplicationMaintenanceCommand
{
    /// <summary>
    /// Represents help command.
    /// </summary>
    /// <returns>A new command instance.</returns>
    public static ApplicationMaintenanceCommand HelpCommand()
    {
        var help = new HelpOption();
        var command = new ApplicationMaintenanceCommand("help")
        {
            Description = help.Description
        };

        command.SetAction(help.Invoke);
        return command;
    }
}

file static class HelpOptionExtensions
{
    public static int Invoke(this HelpOption option, ParseResult result)
    {
        var root = result.RootCommandResult;
        return result.Configuration.Invoke(option.Name);
    }
}