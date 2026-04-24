namespace BackupZCrypt.Terminal;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Terminal.Commands;
using BackupZCrypt.Terminal.Rendering;
using BackupZCrypt.Terminal.Resources;
using Spectre.Console;

internal sealed class TerminalApplication(
    BackupCommand backupCommand,
    BackupSettingsCommand backupSettingsCommand,
    AlgorithmInfoCommand algorithmInfoCommand)
{
    public async Task RunAsync()
    {
        while (true)
        {
            AnsiConsole.Clear();
            BannerRenderer.Print();

            string choice = await AnsiConsole.PromptAsync(
                new SelectionPrompt<string>()
                    .HighlightStyle(Style.Parse("bold cyan"))
                    .AddChoices(
                        Messages.MenuCreateBackup,
                        Messages.MenuUpdateBackup,
                        Messages.MenuRestoreBackup,
                        Messages.MenuBackupSettings,
                        Messages.MenuAlgorithmInfo,
                        Messages.MenuExit));

            if (choice == Messages.MenuCreateBackup)
            {
                await backupCommand.ExecuteAsync(EncryptOperation.Encrypt);
            }
            else if (choice == Messages.MenuUpdateBackup)
            {
                await backupCommand.ExecuteAsync(EncryptOperation.Update);
            }
            else if (choice == Messages.MenuRestoreBackup)
            {
                await backupCommand.ExecuteAsync(EncryptOperation.Decrypt);
            }
            else if (choice == Messages.MenuBackupSettings)
            {
                await backupSettingsCommand.ExecuteAsync();
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
