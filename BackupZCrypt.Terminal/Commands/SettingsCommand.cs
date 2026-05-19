namespace BackupZCrypt.Terminal.Commands;

using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Terminal.Resources;
using Spectre.Console;
using System.Globalization;

internal sealed class SettingsCommand(
    ISettingsService settingsService,
    IReadOnlyList<IEncryptionAlgorithmStrategy> encryptionStrategies,
    IReadOnlyList<IKeyDerivationAlgorithmStrategy> keyDerivationStrategies,
    IReadOnlyList<ICompressionStrategy> compressionStrategies)
{
    private static readonly (string DisplayName, string? Code)[] SupportedLanguages =
    [
        ("English", "en"),
        ("Español", "es"),
    ];

    public async Task ExecuteAsync()
    {
        BackupCreationSettings settings;
        LanguageSettings languageSettings;

        try
        {
            settings = await settingsService.GetOrCreateAsync<BackupCreationSettings>();
            languageSettings = await settingsService.GetOrCreateAsync<LanguageSettings>();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(
                $"[red]{string.Format(Messages.UnexpectedErrorFormat, Markup.Escape(ex.Message))}[/]");
            return;
        }

        PrintHeader();

        while (true)
        {
            this.PrintSummary(settings, languageSettings);

            string action;

            try
            {
                action = await AnsiConsole.PromptAsync(
                    new SelectionPrompt<string>()
                        .Title($"[green]{Messages.SettingsActionPrompt}[/]")
                        .HighlightStyle(Style.Parse("bold cyan"))
                        .AddChoices(
                            Messages.SettingsEncryptionAlgorithmOption,
                            Messages.SettingsKeyDerivationOption,
                            Messages.SettingsCompressionOption,
                            Messages.SettingsLanguageOption,
                            Messages.SettingsResetOption,
                            Messages.SettingsBack));
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (action == Messages.SettingsBack)
            {
                try
                {
                    await settingsService.SaveAsync(settings);
                    return;
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine(
                        $"[red]{string.Format(Messages.UnexpectedErrorFormat, Markup.Escape(ex.Message))}[/]");
                    return;
                }
            }

            if (action == Messages.SettingsLanguageOption)
            {
                languageSettings = await this.PromptLanguageAsync(languageSettings);
                AnsiConsole.WriteLine();
                continue;
            }

            settings = action switch
            {
                var value when value == Messages.SettingsEncryptionAlgorithmOption
                    =>
                PromptOptionalStrategy(
                            Messages.EncryptionAlgorithmPrompt,
                            encryptionStrategies,
                            strategy => $"{strategy.DisplayName} — {strategy.Summary}",
                            Messages.NoneNoEncryption) is { } encryptionStrategy
                        ? settings with
                        {
                            EncryptionAlgorithm = encryptionStrategy.Id,
                        }
                        : settings with
                        {
                            EncryptionAlgorithm = EncryptionAlgorithm.None,
                        },
                var value when value == Messages.SettingsKeyDerivationOption
                    => settings with
                    {
                        KeyDerivationAlgorithm = PromptStrategy(
                            Messages.KeyDerivationAlgorithmPrompt,
                            keyDerivationStrategies,
                            strategy => $"{strategy.DisplayName} — {strategy.Summary}").Id,
                    },
                var value when value == Messages.SettingsCompressionOption
                    => settings with
                    {
                        CompressionMode = PromptOptionalStrategy(
                            Messages.CompressionModePrompt,
                            compressionStrategies,
                            strategy => $"{strategy.DisplayName} — {strategy.Summary}",
                            Messages.NoneNoCompression)?.Id ?? CompressionMode.None,
                    },
                _ => BackupCreationSettings.DefaultValue,
            };

            if (action == Messages.SettingsResetOption)
            {
                AnsiConsole.MarkupLine($"[green]{Messages.SettingsReset}[/]");
            }

            AnsiConsole.WriteLine();
        }
    }

    public static void ApplyLanguage(string? languageCode)
    {
        var culture = string.IsNullOrWhiteSpace(languageCode)
            ? CultureInfo.InstalledUICulture
            : new CultureInfo(languageCode);

        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private static void PrintHeader()
    {
        AnsiConsole.Write(new Rule($"[bold cyan]{Messages.Settings}[/]").RuleStyle(Style.Parse("grey")));
        AnsiConsole.WriteLine();
    }

    private async Task<LanguageSettings> PromptLanguageAsync(LanguageSettings current)
    {
        var choices = new List<string> { Messages.LanguageSystemDefault };
        choices.AddRange(SupportedLanguages.Select(l => l.DisplayName));

        string selected;

        try
        {
            selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"[green]{Messages.LanguagePrompt}[/]")
                    .HighlightStyle(Style.Parse("bold cyan"))
                    .AddChoices(choices));
        }
        catch (OperationCanceledException)
        {
            return current;
        }

        string? selectedCode = null;

        if (selected != Messages.LanguageSystemDefault)
        {
            selectedCode = SupportedLanguages
                .First(l => l.DisplayName == selected).Code;
        }

        var newSettings = new LanguageSettings(selectedCode);

        try
        {
            await settingsService.SaveAsync(newSettings);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(
                $"[red]{string.Format(Messages.UnexpectedErrorFormat, Markup.Escape(ex.Message))}[/]");
            return current;
        }

        AnsiConsole.MarkupLine($"[yellow]{Messages.LanguageChanged}[/]");

        return newSettings;
    }

    private void PrintSummary(BackupCreationSettings settings, LanguageSettings languageSettings)
    {
        var summaryTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title($"[bold cyan]{Messages.Settings}[/]")
            .AddColumn(new TableColumn($"[bold]{Messages.Setting}[/]").LeftAligned())
            .AddColumn(new TableColumn($"[bold]{Messages.Value}[/]").LeftAligned());

        summaryTable.AddRow(
            Messages.SettingsFileLabel,
            Markup.Escape(settingsService.GetFilePath<BackupCreationSettings>()));
        summaryTable.AddRow(
            Messages.EncryptionLabel,
            Markup.Escape(this.ResolveEncryptionDisplayName(settings)));

        if (settings.EncryptionAlgorithm != EncryptionAlgorithm.None)
        {
            summaryTable.AddRow(
                Messages.KeyDerivationLabel,
                Markup.Escape(this.ResolveKeyDerivationStrategy(settings.KeyDerivationAlgorithm).DisplayName));
        }

        summaryTable.AddRow(
            Messages.CompressionLabel,
            Markup.Escape(this.ResolveCompressionDisplayName(settings.CompressionMode)));
        summaryTable.AddRow(
            Messages.LanguageLabel,
            Markup.Escape(ResolveLanguageDisplayName(languageSettings.LanguageCode)));

        AnsiConsole.Write(summaryTable);
        AnsiConsole.WriteLine();
    }

    private static T PromptStrategy<T>(
        string title,
        IReadOnlyList<T> strategies,
        Func<T, string> converter)
        where T : class =>
        AnsiConsole.Prompt(
            new SelectionPrompt<T>()
                .Title($"[green]{title}[/]")
                .HighlightStyle(Style.Parse("bold cyan"))
                .UseConverter(converter)
                .AddChoices(strategies));

    private static T? PromptOptionalStrategy<T>(
        string title,
        IReadOnlyList<T> strategies,
        Func<T, string> converter,
        string noneLabel)
        where T : class
    {
        List<string> displayChoices = [noneLabel, .. strategies.Select(converter)];

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"[green]{title}[/]")
                .HighlightStyle(Style.Parse("bold cyan"))
                .AddChoices(displayChoices));

        if (selected == noneLabel)
        {
            return null;
        }

        var index = displayChoices.IndexOf(selected) - 1;
        return strategies[index];
    }

    private string ResolveEncryptionDisplayName(BackupCreationSettings settings)
    {
        if (settings.EncryptionAlgorithm == EncryptionAlgorithm.None)
        {
            return Messages.NoneNoEncryption;
        }

        return encryptionStrategies.FirstOrDefault(strategy => strategy.Id == settings.EncryptionAlgorithm)?.DisplayName
            ?? throw new InvalidOperationException(
                $"No encryption strategy is registered for '{settings.EncryptionAlgorithm}'.");
    }

    private IKeyDerivationAlgorithmStrategy ResolveKeyDerivationStrategy(
        KeyDerivationAlgorithm algorithm) =>
        keyDerivationStrategies.FirstOrDefault(strategy => strategy.Id == algorithm)
        ?? throw new InvalidOperationException(
            $"No key derivation strategy is registered for '{algorithm}'.");

    private string ResolveCompressionDisplayName(CompressionMode mode)
    {
        if (mode == CompressionMode.None)
        {
            return Messages.NoneNoCompression;
        }

        return compressionStrategies.FirstOrDefault(strategy => strategy.Id == mode)?.DisplayName
            ?? throw new InvalidOperationException(
                $"No compression strategy is registered for '{mode}'.");
    }

    private static string ResolveLanguageDisplayName(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return Messages.LanguageSystemDefault;
        }

        var match = SupportedLanguages
            .FirstOrDefault(l => string.Equals(l.Code, languageCode, StringComparison.OrdinalIgnoreCase));

        return match.DisplayName ?? languageCode;
    }
}
