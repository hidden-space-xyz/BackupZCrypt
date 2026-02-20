namespace CloudZCrypt.Terminal;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Terminal.Commands;
using CloudZCrypt.Terminal.Rendering;
using CloudZCrypt.Terminal.Resources;
using Spectre.Console;

internal sealed class TerminalApplication(
    EncryptCommand encryptCommand,
    GeneratePasswordCommand generatePasswordCommand,
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
                        Messages.MenuEncrypt,
                        Messages.MenuDecrypt,
                        Messages.MenuGeneratePassword,
                        Messages.MenuAlgorithmInfo,
                        Messages.MenuExit));

            if (choice == Messages.MenuEncrypt)
            {
                await encryptCommand.ExecuteAsync(EncryptOperation.Encrypt);
            }
            else if (choice == Messages.MenuDecrypt)
            {
                await encryptCommand.ExecuteAsync(EncryptOperation.Decrypt);
            }
            else if (choice == Messages.MenuGeneratePassword)
            {
                generatePasswordCommand.Execute();
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
            Console.ReadKey(true);
        }
    }
}
