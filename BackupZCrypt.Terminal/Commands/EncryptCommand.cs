namespace BackupZCrypt.Terminal.Commands;

using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Password;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Exceptions;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.FileCrypt;
using BackupZCrypt.Terminal.Rendering;
using BackupZCrypt.Terminal.Resources;
using Spectre.Console;

internal sealed class EncryptCommand(
    IFileCryptOrchestrator orchestrator,
    IPasswordService passwordService,
    IReadOnlyList<IEncryptionAlgorithmStrategy> encryptionStrategies,
    IReadOnlyList<IKeyDerivationAlgorithmStrategy> keyDerivationStrategies,
    IReadOnlyList<INameObfuscationStrategy> nameObfuscationStrategies,
    IReadOnlyList<ICompressionStrategy> compressionStrategies)
{
    public async Task ExecuteAsync(EncryptOperation operation)
    {
        string operationName =
            operation == EncryptOperation.Encrypt ? Messages.Encrypt : Messages.Decrypt;
        string operationIngName =
            operation == EncryptOperation.Encrypt ? Messages.Encrypting : Messages.Decrypting;

        AnsiConsole.Write(
            new Rule($"[bold cyan]{operationName}[/]").RuleStyle(Style.Parse("grey")));
        AnsiConsole.WriteLine();

        string sourcePath = PromptSourcePath();
        string destinationPath = PromptDestinationPath();

        if (operation == EncryptOperation.Encrypt)
        {
            (string password, string confirmPassword) = PromptPasswords();

            this.PrintPasswordStrength(password);

            IEncryptionAlgorithmStrategy selectedEncryption = PromptStrategy(
                Messages.EncryptionAlgorithmPrompt,
                encryptionStrategies,
                s => $"{s.DisplayName} — {s.Summary}");
            IKeyDerivationAlgorithmStrategy selectedKdf = PromptStrategy(
                Messages.KeyDerivationAlgorithmPrompt,
                keyDerivationStrategies,
                s => $"{s.DisplayName} — {s.Summary}");
            INameObfuscationStrategy selectedObfuscation = PromptStrategy(
                Messages.NameObfuscationModePrompt,
                nameObfuscationStrategies,
                s => $"{s.DisplayName} — {s.Summary}");
            ICompressionStrategy selectedCompression = PromptStrategy(
                Messages.CompressionModePrompt,
                compressionStrategies,
                s => $"{s.DisplayName} — {s.Summary}");

            PrintSummary(
                operationName,
                sourcePath,
                destinationPath,
                selectedEncryption,
                selectedKdf,
                selectedObfuscation,
                selectedCompression);

            if (
                !await AnsiConsole.ConfirmAsync(
                    $"[yellow]{string.Format(Messages.ProceedConfirmFormat, operationName.ToUpperInvariant())}[/]"))
            {
                AnsiConsole.MarkupLine($"[grey]{Messages.OperationCancelled}[/]");
                return;
            }

            FileCryptRequest request = new(
                sourcePath,
                destinationPath,
                password,
                confirmPassword,
                selectedEncryption.Id,
                selectedKdf.Id,
                operation,
                selectedObfuscation.Id,
                selectedCompression.Id,
                ProceedOnWarnings: false);

            await RunOperationAsync(request, operationName, operationIngName);
        }
        else
        {
            string password = PromptPassword();

            if (
                !await AnsiConsole.ConfirmAsync(
                    $"[yellow]{string.Format(Messages.ProceedConfirmFormat, operationName.ToUpperInvariant())}[/]"))
            {
                AnsiConsole.MarkupLine($"[grey]{Messages.OperationCancelled}[/]");
                return;
            }

            FileCryptRequest request = new(
                sourcePath,
                destinationPath,
                password,
                password,
                EncryptionAlgorithm.Aes,
                KeyDerivationAlgorithm.Argon2id,
                operation,
                NameObfuscationMode.None);

            await RunOperationAsync(request, operationName, operationIngName);
        }
    }

    private static void PrintFailure(string operationName, string[] errors) =>
        AnsiConsole.MarkupLine(
            $"[red]{string.Format(Messages.FailedFormat, operationName, Markup.Escape(string.Join(", ", errors)))}[/]");

    private static void PrintSummary(
        string operationName,
        string sourcePath,
        string destinationPath,
        IEncryptionAlgorithmStrategy encryption,
        IKeyDerivationAlgorithmStrategy kdf,
        INameObfuscationStrategy obfuscation,
        ICompressionStrategy compression)
    {
        AnsiConsole.WriteLine();
        Table summaryTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title($"[bold cyan]{operationName} {Messages.SummaryTitle}[/]")
            .AddColumn(new TableColumn($"[bold]{Messages.Setting}[/]").LeftAligned())
            .AddColumn(new TableColumn($"[bold]{Messages.Value}[/]").LeftAligned());

        summaryTable.AddRow(Messages.Source, Markup.Escape(sourcePath));
        summaryTable.AddRow(Messages.Destination, Markup.Escape(destinationPath));
        summaryTable.AddRow(Messages.EncryptionLabel, Markup.Escape(encryption.DisplayName));
        summaryTable.AddRow(Messages.KeyDerivationLabel, Markup.Escape(kdf.DisplayName));
        summaryTable.AddRow(Messages.NameObfuscationLabel, Markup.Escape(obfuscation.DisplayName));
        summaryTable.AddRow(Messages.CompressionLabel, Markup.Escape(compression.DisplayName));
        AnsiConsole.Write(summaryTable);
        AnsiConsole.WriteLine();
    }

    private static void PrintValidationErrors(IReadOnlyList<string> errors)
    {
        AnsiConsole.MarkupLine($"[red]{Messages.ValidationErrors}[/]");
        foreach (string error in errors)
        {
            AnsiConsole.MarkupLine($"  [red]❌ {Markup.Escape(error)}[/]");
        }
    }

    private static string PromptDestinationPath() =>
        AnsiConsole.Prompt(
            new TextPrompt<string>($"[green]{Messages.DestinationPathPrompt}[/]:")
                .ValidationErrorMessage($"[red]{Messages.PathCannotBeEmpty}[/]")
                .Validate(p =>
                    !string.IsNullOrWhiteSpace(p)
                        ? ValidationResult.Success()
                        : ValidationResult.Error($"[red]{Messages.PleaseEnterDestinationPath}[/]")));

    private static string PromptPassword() =>
        AnsiConsole.Prompt(
            new TextPrompt<string>($"[green]{Messages.PasswordPrompt}[/]:")
                .Secret()
                .ValidationErrorMessage($"[red]{Messages.PasswordCannotBeEmpty}[/]")
                .Validate(p =>
                    !string.IsNullOrWhiteSpace(p)
                        ? ValidationResult.Success()
                        : ValidationResult.Error($"[red]{Messages.PasswordCannotBeEmpty}[/]")));

    private static (string Password, string ConfirmPassword) PromptPasswords()
    {
        string password = PromptPassword();

        string confirmPassword = AnsiConsole.Prompt(
            new TextPrompt<string>($"[green]{Messages.ConfirmPasswordPrompt}[/]:").Secret());

        return (password, confirmPassword);
    }

    private static string PromptSourcePath() =>
        AnsiConsole.Prompt(
            new TextPrompt<string>(
                $"[green]{Messages.SourcePathPrompt}[/] {Messages.SourcePathHint}")
                .ValidationErrorMessage($"[red]{Messages.PathCannotBeEmpty}[/]")
                .Validate(p =>
                    !string.IsNullOrWhiteSpace(p) && (File.Exists(p) || Directory.Exists(p))
                        ? ValidationResult.Success()
                        : ValidationResult.Error($"[red]{Messages.PathDoesNotExist}[/]")));

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

    private async Task<FileCryptResult?> HandleWarningsAsync(
        FileCryptResult response,
        FileCryptRequest request,
        string operationName,
        string operationIngName,
        CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine($"⚠  [yellow]{Messages.WarningsLabel}[/]");
        foreach (string warning in response.Warnings)
        {
            AnsiConsole.MarkupLine($"  - [yellow]{Markup.Escape(warning)}[/]");
        }

        AnsiConsole.WriteLine();

        if (
            !await AnsiConsole.ConfirmAsync($"[yellow]{string.Format(Messages.ContinueDespiteWarningsFormat, operationName.ToUpperInvariant())}[/]", cancellationToken: cancellationToken))
        {
            AnsiConsole.MarkupLine($"[grey]{Messages.OperationCancelled}[/]");
            return null;
        }

        FileCryptRequest proceedRequest = request with { ProceedOnWarnings = true };

        Result<FileCryptResult> result = await ProgressRunner.RunAsync(
            orchestrator,
            proceedRequest,
            operationName,
            operationIngName,
            cancellationToken);

        if (!result.IsSuccess)
        {
            PrintFailure(operationName, result.Errors);
            return null;
        }

        return result.Value;
    }

    private void PrintPasswordStrength(string password)
    {
        PasswordStrengthAnalysis strength = passwordService.AnalyzePasswordStrength(password);
        string strengthColor = strength.Strength switch
        {
            PasswordStrength.VeryWeak => "red",
            PasswordStrength.Weak => "red",
            PasswordStrength.Fair => "yellow",
            PasswordStrength.Good => "green",
            PasswordStrength.Strong => "bold green",
            _ => "white",
        };
        AnsiConsole.MarkupLine(
            $"  {Messages.PasswordStrengthLabel} [{strengthColor}]{Markup.Escape(strength.Description)}[/]");
        AnsiConsole.WriteLine();
    }

    private async Task RunOperationAsync(
        FileCryptRequest request,
        string operationName,
        string operationIngName)
    {
        AnsiConsole.WriteLine();

        using CancellationTokenSource cts = new();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            AnsiConsole.MarkupLine($"[yellow]{Messages.Cancelling}[/]");
        };

        try
        {
            Result<FileCryptResult> result = await ProgressRunner.RunAsync(
                orchestrator,
                request,
                operationName,
                operationIngName,
                cts.Token);

            if (!result.IsSuccess)
            {
                PrintFailure(operationName, result.Errors);
                return;
            }

            FileCryptResult? response = result.Value;

            if (response.HasErrors && response.TotalFiles == 0 && response.ProcessedFiles == 0)
            {
                PrintValidationErrors(response.Errors);
                return;
            }

            if (response.HasWarnings && !request.ProceedOnWarnings)
            {
                response = await HandleWarningsAsync(
                    response,
                    request,
                    operationName,
                    operationIngName,
                    cts.Token);

                if (response is null)
                {
                    return;
                }
            }

            ResultRenderer.Print(response, operationName);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine($"[yellow]{Messages.OperationCancelledByUser}[/]");
        }
        catch (EncryptionException ex)
        {
            AnsiConsole.MarkupLine(
                $"[red]❌ {Markup.Escape(ex.Code.ToString())}: {Markup.Escape(ex.Message)}[/]");
        }
        catch (ValidationException ex)
        {
            AnsiConsole.MarkupLine(
                $"[red]{string.Format(Messages.ValidationErrorFormat, Markup.Escape(ex.Message))}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(
                $"[red]{string.Format(Messages.UnexpectedErrorFormat, Markup.Escape(ex.Message))}[/]");
        }
    }
}
