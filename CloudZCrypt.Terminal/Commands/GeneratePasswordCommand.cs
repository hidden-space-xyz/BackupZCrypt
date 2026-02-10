using CloudZCrypt.Application.Services.Interfaces;
using CloudZCrypt.Application.ValueObjects.Password;
using CloudZCrypt.Domain.Enums;
using Spectre.Console;

namespace CloudZCrypt.Terminal.Commands;

internal sealed class GeneratePasswordCommand(IPasswordService passwordService)
{
    public void Execute()
    {
        AnsiConsole.Write(
            new Rule("[bold cyan]Password Generator[/]").RuleStyle(Style.Parse("grey"))
        );
        AnsiConsole.WriteLine();

        int length = PromptLength();
        PasswordGenerationOptions options = PromptOptions();

        string generated = passwordService.GeneratePassword(length, options);
        PasswordStrengthAnalysis analysis = passwordService.AnalyzePasswordStrength(generated);

        PrintGeneratedPassword(generated, analysis, length);
    }

    private static int PromptLength() =>
        AnsiConsole.Prompt(
            new TextPrompt<int>("[green]Password length[/] (16–256):")
                .DefaultValue(128)
                .Validate(l =>
                    l is >= 16 and <= 256
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Length must be between 16 and 256[/]")
                )
        );

    private static PasswordGenerationOptions PromptOptions()
    {
        List<string> optionChoices = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("[green]Include:[/]")
                .Required()
                .InstructionsText(
                    "[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to confirm)[/]"
                )
                .AddChoices(
                    "Uppercase (A-Z)",
                    "Lowercase (a-z)",
                    "Numbers (0-9)",
                    "Special characters (!@#$)"
                )
                .Select("Uppercase (A-Z)")
                .Select("Lowercase (a-z)")
                .Select("Numbers (0-9)")
                .Select("Special characters (!@#$)")
        );

        PasswordGenerationOptions options = PasswordGenerationOptions.None;
        if (optionChoices.Contains("Uppercase (A-Z)"))
            options |= PasswordGenerationOptions.IncludeUppercase;
        if (optionChoices.Contains("Lowercase (a-z)"))
            options |= PasswordGenerationOptions.IncludeLowercase;
        if (optionChoices.Contains("Numbers (0-9)"))
            options |= PasswordGenerationOptions.IncludeNumbers;
        if (optionChoices.Contains("Special characters (!@#$)"))
            options |= PasswordGenerationOptions.IncludeSpecialCharacters;

        return options;
    }

    private static void PrintGeneratedPassword(
        string generated,
        PasswordStrengthAnalysis analysis,
        int length
    )
    {
        AnsiConsole.WriteLine();
        Panel passwordPanel = new(
            new Rows(
                new Markup($"[bold]{Markup.Escape(generated)}[/]"),
                new Text(""),
                new Markup(
                    $"[dim]Strength:[/] [bold green]{analysis.Strength}[/] — {Markup.Escape(analysis.Description)}"
                ),
                new Markup($"[dim]Length:[/]   {length} characters")
            )
        )
        {
            Header = new PanelHeader("[bold cyan]🔐 Generated Password[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Cyan1),
            Padding = new Padding(1, 1),
        };

        AnsiConsole.Write(passwordPanel);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(
            "[yellow]⚠ Store this password securely — it cannot be recovered if lost![/]"
        );
    }
}
