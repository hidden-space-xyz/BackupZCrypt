namespace BackupZCrypt.Terminal.Commands;

using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Terminal.Resources;
using Spectre.Console;

internal sealed class BackupSettingsCommand(
    IBackupCreationSettingsService backupCreationSettingsService,
    IReadOnlyList<IEncryptionAlgorithmStrategy> encryptionStrategies,
    IReadOnlyList<IKeyDerivationAlgorithmStrategy> keyDerivationStrategies,
    IReadOnlyList<INameObfuscationStrategy> nameObfuscationStrategies,
    IReadOnlyList<ICompressionStrategy> compressionStrategies)
{
    public async Task ExecuteAsync()
    {
        BackupCreationSettings settings;

        try
        {
            settings = await backupCreationSettingsService.GetOrCreateAsync();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(
                $"[red]{string.Format(Messages.UnexpectedErrorFormat, Markup.Escape(ex.Message))}[/]");
            return;
        }

        AnsiConsole.Write(
            new Rule($"[bold cyan]{Messages.BackupSettings}[/]").RuleStyle(Style.Parse("grey")));
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[dim]{Messages.BackupSettingsPersistenceNotice}[/]");
        AnsiConsole.WriteLine();

        while (true)
        {
            this.PrintSummary(settings);

            string action;

            try
            {
                action = await AnsiConsole.PromptAsync(
                    new SelectionPrompt<string>()
                        .Title($"[green]{Messages.BackupSettingsActionPrompt}[/]")
                        .HighlightStyle(Style.Parse("bold cyan"))
                        .AddChoices(
                            Messages.BackupSettingsEncryptionAlgorithmOption,
                            Messages.BackupSettingsKeyDerivationOption,
                            Messages.BackupSettingsNameObfuscationOption,
                            Messages.BackupSettingsCompressionOption,
                            Messages.BackupSettingsResetOption,
                            Messages.BackupSettingsBack));
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (action == Messages.BackupSettingsBack)
            {
                try
                {
                    await backupCreationSettingsService.SaveAsync(settings);
                    return;
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine(
                        $"[red]{string.Format(Messages.UnexpectedErrorFormat, Markup.Escape(ex.Message))}[/]");
                    return;
                }
            }

            settings = action switch
            {
                var value when value == Messages.BackupSettingsEncryptionAlgorithmOption
                    =>
                PromptOptionalStrategy(
                            Messages.EncryptionAlgorithmPrompt,
                            encryptionStrategies,
                            strategy => $"{strategy.DisplayName} — {strategy.Summary}",
                            Messages.NoneNoEncryption) is { } encryptionStrategy
                        ? settings with
                        {
                            UseEncryption = true,
                            EncryptionAlgorithm = encryptionStrategy.Id,
                        }
                        : settings with
                        {
                            UseEncryption = false,
                            NameObfuscationMode = NameObfuscationMode.None,
                        },
                var value when value == Messages.BackupSettingsKeyDerivationOption
                    => settings with
                    {
                        KeyDerivationAlgorithm = PromptStrategy(
                            Messages.KeyDerivationAlgorithmPrompt,
                            keyDerivationStrategies,
                            strategy => $"{strategy.DisplayName} — {strategy.Summary}").Id,
                    },
                var value when value == Messages.BackupSettingsNameObfuscationOption
                    => settings with
                    {
                        NameObfuscationMode = PromptOptionalStrategy(
                            Messages.NameObfuscationModePrompt,
                            nameObfuscationStrategies,
                            strategy => $"{strategy.DisplayName} — {strategy.Summary}",
                            Messages.NoneNoObfuscation)?.Id ?? NameObfuscationMode.None,
                    },
                var value when value == Messages.BackupSettingsCompressionOption
                    => settings with
                    {
                        CompressionMode = PromptOptionalStrategy(
                            Messages.CompressionModePrompt,
                            compressionStrategies,
                            strategy => $"{strategy.DisplayName} — {strategy.Summary}",
                            Messages.NoneNoCompression)?.Id ?? CompressionMode.None,
                    },
                _ => BackupCreationSettings.Default,
            };

            if (action == Messages.BackupSettingsResetOption)
            {
                AnsiConsole.MarkupLine($"[green]{Messages.BackupSettingsReset}[/]");
            }

            AnsiConsole.WriteLine();
        }
    }

    private void PrintSummary(BackupCreationSettings settings)
    {
        var summaryTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title($"[bold cyan]{Messages.BackupSettings}[/]")
            .AddColumn(new TableColumn($"[bold]{Messages.Setting}[/]").LeftAligned())
            .AddColumn(new TableColumn($"[bold]{Messages.Value}[/]").LeftAligned());

        summaryTable.AddRow(
            Messages.SettingsFileLabel,
            Markup.Escape(backupCreationSettingsService.SettingsFilePath));
        summaryTable.AddRow(
            Messages.EncryptionLabel,
            Markup.Escape(this.ResolveEncryptionDisplayName(settings)));

        if (settings.UseEncryption)
        {
            summaryTable.AddRow(
                Messages.KeyDerivationLabel,
                Markup.Escape(this.ResolveKeyDerivationStrategy(settings.KeyDerivationAlgorithm).DisplayName));
            summaryTable.AddRow(
                Messages.NameObfuscationLabel,
                Markup.Escape(this.ResolveNameObfuscationDisplayName(settings.NameObfuscationMode)));
        }

        summaryTable.AddRow(
            Messages.CompressionLabel,
            Markup.Escape(this.ResolveCompressionDisplayName(settings.CompressionMode)));

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
        if (!settings.UseEncryption)
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

    private string ResolveNameObfuscationDisplayName(NameObfuscationMode mode)
    {
        if (mode == NameObfuscationMode.None)
        {
            return Messages.NoneNoObfuscation;
        }

        return nameObfuscationStrategies.FirstOrDefault(strategy => strategy.Id == mode)?.DisplayName
            ?? throw new InvalidOperationException(
                $"No name obfuscation strategy is registered for '{mode}'.");
    }

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
}
