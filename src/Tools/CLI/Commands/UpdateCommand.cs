using DES.CLI.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DES.CLI.Commands;

public sealed class UpdateCommand : AsyncCommand
{
    protected override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine($"[bold {DesConstants.AccentColor}]Updating DES tools...[/]");
        AnsiConsole.WriteLine();

        bool cliSuccess = await RunUpdateStepAsync(
            "Updating DES CLI",
            "dotnet", $"tool update -g {DesConstants.CliPackageId}",
            cancellationToken).ConfigureAwait(false);

        bool templateSuccess = await RunUpdateStepAsync(
            "Updating DES template",
            "dotnet", $"new install {DesConstants.TemplatePackageId}",
            cancellationToken).ConfigureAwait(false);

        AnsiConsole.WriteLine();

        bool allSuccess = cliSuccess && templateSuccess;
        if (allSuccess)
        {
            AnsiConsole.MarkupLine($"[{DesConstants.SuccessColor}]All updates complete.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[{DesConstants.WarningColor}]Some updates failed. Check the output above.[/]");
        }

        return allSuccess ? 0 : 1;
    }

    private static async Task<bool> RunUpdateStepAsync(
        string description, string command, string arguments, CancellationToken cancellationToken)
    {
        return await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse(DesConstants.AccentColor))
            .StartAsync(description, async _ =>
            {
                int exitCode = await ProcessRunner.RunAsync(
                    command, arguments,
                    showOutput: false,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (exitCode == 0)
                {
                    AnsiConsole.MarkupLine($"  [{DesConstants.SuccessColor}]{description}: done[/]");
                    return true;
                }

                AnsiConsole.MarkupLine($"  [{DesConstants.ErrorColor}]{description}: failed (exit code {exitCode})[/]");
                return false;
            }).ConfigureAwait(false);
    }
}
