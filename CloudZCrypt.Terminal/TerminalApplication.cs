using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Terminal.Commands;
using CloudZCrypt.Terminal.Rendering;
using Spectre.Console;

namespace CloudZCrypt.Terminal;

internal sealed class TerminalApplication(
    EncryptCommand encryptCommand,
    GeneratePasswordCommand generatePasswordCommand,
    AlgorithmInfoCommand algorithmInfoCommand
)
{
    public async Task RunAsync()
    {
        while (true)
        {
            AnsiConsole.Clear();
            BannerRenderer.Print();

            string choice = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold cyan]Main Menu[/]")
                    .HighlightStyle(Style.Parse("bold green"))
                    .AddChoices(
                        "🔒 Encrypt",
                        "🔓 Decrypt",
                        "🔑 Generate Password",
                        "ℹ️ Algorithm Info",
                        "❌ Exit"
                    )
            );

            switch (choice)
            {
                case "🔒 Encrypt":
                    await encryptCommand.ExecuteAsync(EncryptOperation.Encrypt);
                    break;
                case "🔓 Decrypt":
                    await encryptCommand.ExecuteAsync(EncryptOperation.Decrypt);
                    break;
                case "🔑 Generate Password":
                    generatePasswordCommand.Execute();
                    break;
                case "ℹ️ Algorithm Info":
                    algorithmInfoCommand.Execute();
                    break;
                case "❌ Exit":
                    AnsiConsole.MarkupLine("[grey]Goodbye![/]");
                    return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press any key to return to the menu…[/]");
            Console.ReadKey(true);
        }
    }
}
