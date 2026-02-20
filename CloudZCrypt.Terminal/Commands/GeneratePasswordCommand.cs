namespace CloudZCrypt.Terminal.Commands;

using CloudZCrypt.Application.Services.Interfaces;
using CloudZCrypt.Application.ValueObjects.Password;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Terminal.Resources;
using Spectre.Console;

internal sealed class GeneratePasswordCommand(IPasswordService passwordService)
{
    public void Execute()
    {
        AnsiConsole.Write(
            new Rule($"[bold cyan]{Messages.PasswordGenerator}[/]").RuleStyle(Style.Parse("grey")));
        AnsiConsole.WriteLine();

        int length = PromptLength();
        PasswordGenerationOptions options = PromptOptions();

        string generated = passwordService.GeneratePassword(length, options);
        PasswordStrengthAnalysis analysis = passwordService.AnalyzePasswordStrength(generated);

        PrintGeneratedPassword(generated, analysis, length);
    }

    private static int PromptLength() =>
        AnsiConsole.Prompt(
            new TextPrompt<int>(
                $"[green]{Messages.PasswordLengthPrompt}[/] {Messages.PasswordLengthRange}")
                .DefaultValue(128)
                .Validate(l =>
                    l is >= 16 and <= 256
                        ? ValidationResult.Success()
                        : ValidationResult.Error($"[red]{Messages.PasswordLengthError}[/]")));

    private static PasswordGenerationOptions PromptOptions()
    {
        List<string> optionChoices = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title($"[green]{Messages.IncludePrompt}[/]")
                .Required()
                .InstructionsText($"[grey]{Messages.ToggleInstructions}[/]")
                .AddChoices(
                    Messages.OptionUppercase,
                    Messages.OptionLowercase,
                    Messages.OptionNumbers,
                    Messages.OptionSpecialChars)
                .Select(Messages.OptionUppercase)
                .Select(Messages.OptionLowercase)
                .Select(Messages.OptionNumbers)
                .Select(Messages.OptionSpecialChars));

        PasswordGenerationOptions options = PasswordGenerationOptions.None;
        if (optionChoices.Contains(Messages.OptionUppercase))
        {
            options |= PasswordGenerationOptions.IncludeUppercase;
        }

        if (optionChoices.Contains(Messages.OptionLowercase))
        {
            options |= PasswordGenerationOptions.IncludeLowercase;
        }

        if (optionChoices.Contains(Messages.OptionNumbers))
        {
            options |= PasswordGenerationOptions.IncludeNumbers;
        }

        if (optionChoices.Contains(Messages.OptionSpecialChars))
        {
            options |= PasswordGenerationOptions.IncludeSpecialCharacters;
        }

        return options;
    }

    private static void PrintGeneratedPassword(
        string generated,
        PasswordStrengthAnalysis analysis,
        int length)
    {
        string strengthColor = analysis.Strength switch
        {
            PasswordStrength.VeryWeak => "red",
            PasswordStrength.Weak => "red",
            PasswordStrength.Fair => "yellow",
            PasswordStrength.Good => "green",
            PasswordStrength.Strong => "bold green",
            _ => "white",
        };

        AnsiConsole.WriteLine();
        Panel passwordPanel = new(
            new Rows(
                new Markup($"[bold]{Markup.Escape(generated)}[/]"),
                new Text(string.Empty),
                new Markup(
                    $"[dim]{Messages.StrengthLabel}[/] [{strengthColor}]{Markup.Escape(analysis.Description)}[/]"),
                new Markup($"[dim]{Messages.LengthLabel}[/]   {length} {Messages.CharactersLabel}")))
        {
            Header = new PanelHeader($"{Messages.GeneratedPasswordHeader}"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Cyan1),
            Padding = new Padding(1, 1),
        };

        AnsiConsole.Write(passwordPanel);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"⚠  [yellow]{Messages.PasswordStorageWarning}[/]");
    }
}
