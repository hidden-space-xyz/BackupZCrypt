namespace BackupZCrypt.Terminal;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Terminal.Commands;
using BackupZCrypt.Terminal.Rendering;
using BackupZCrypt.Terminal.Resources;
using Spectre.Console;

internal sealed class TerminalApplication(
    BackupCommand backupCommand,
    SettingsCommand settingsCommand,
    AlgorithmInfoCommand algorithmInfoCommand)
{
    public async Task RunAsync()
    {
        while (true)
        {
            AnsiConsole.Clear();
            BannerRenderer.Print();

            var choice = await AnsiConsole.PromptAsync(
                new SelectionPrompt<string>()
                    .HighlightStyle(Style.Parse("bold cyan"))
                    .AddChoices(
                        Messages.MenuCreateBackup,
                        Messages.MenuUpdateBackup,
                        Messages.MenuRestoreBackup,
                        Messages.MenuSettings,
                        Messages.MenuAlgorithmInfo,
                        Messages.MenuExit));

            if (choice == Messages.MenuCreateBackup)
            {
                await backupCommand.ExecuteAsync(BackupOperation.Create);
            }
            else if (choice == Messages.MenuUpdateBackup)
            {
                await backupCommand.ExecuteAsync(BackupOperation.Update);
            }
            else if (choice == Messages.MenuRestoreBackup)
            {
                await backupCommand.ExecuteAsync(BackupOperation.Restore);
            }
            else if (choice == Messages.MenuSettings)
            {
                await settingsCommand.ExecuteAsync();
                continue;
            }
            else if (choice == Messages.MenuAlgorithmInfo)
            {
                algorithmInfoCommand.Execute();
            }
            else if (choice == Messages.MenuExit)
            {
                AnsiConsole.MarkupLine($"[grey]{Messages.Goodbye}[/]");
                return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[dim]{Messages.PressAnyKey}[/]");
            WaitForEscapeKey();
        }
    }

    private static void WaitForEscapeKey()
    {
        while (Console.ReadKey(intercept: true).Key != ConsoleKey.Escape)
        {
        }
    }
}
