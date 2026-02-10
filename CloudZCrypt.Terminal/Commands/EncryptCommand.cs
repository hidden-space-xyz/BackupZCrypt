using CloudZCrypt.Application.Orchestrators.Interfaces;
using CloudZCrypt.Application.Services.Interfaces;
using CloudZCrypt.Application.ValueObjects;
using CloudZCrypt.Application.ValueObjects.Password;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Exceptions;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;
using CloudZCrypt.Terminal.Rendering;
using Spectre.Console;

namespace CloudZCrypt.Terminal.Commands;

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
        string operationName = operation == EncryptOperation.Encrypt ? "Encrypt" : "Decrypt";

        AnsiConsole.Write(new Rule($"[bold cyan]{operationName}[/]").RuleStyle(Style.Parse("grey")));
        AnsiConsole.WriteLine();

        string sourcePath = PromptSourcePath();
        string destinationPath = PromptDestinationPath();
        (string password, string confirmPassword) = PromptPasswords();

        PrintPasswordStrength(password);

        IEncryptionAlgorithmStrategy selectedEncryption = PromptStrategy(
            "Encryption algorithm:", encryptionStrategies, s => $"{s.DisplayName} — {s.Summary}");
        IKeyDerivationAlgorithmStrategy selectedKdf = PromptStrategy(
            "Key derivation algorithm:", keyDerivationStrategies, s => $"{s.DisplayName} — {s.Summary}");
        INameObfuscationStrategy selectedObfuscation = PromptStrategy(
            "Name obfuscation mode:", nameObfuscationStrategies, s => $"{s.DisplayName} — {s.Summary}");
        ICompressionStrategy selectedCompression = PromptStrategy(
            "Compression mode:", compressionStrategies, s => $"{s.DisplayName} — {s.Summary}");

        PrintSummary(operationName, sourcePath, destinationPath,
            selectedEncryption, selectedKdf, selectedObfuscation, selectedCompression);

        if (!AnsiConsole.Confirm($"[yellow]Proceed with {operationName.ToLower()}?[/]"))
        {
            AnsiConsole.MarkupLine("[grey]Operation cancelled.[/]");
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
            ProceedOnWarnings: false
        );

        AnsiConsole.WriteLine();

        using CancellationTokenSource cts = new();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            AnsiConsole.MarkupLine("[yellow]Cancelling…[/]");
        };

        try
        {
            Result<FileCryptResult> result = await ProgressRunner.RunAsync(
                orchestrator, request, operationName, cts.Token);

            if (!result.IsSuccess)
            {
                PrintFailure(operationName, result.Errors);
                return;
            }

            FileCryptResult response = result.Value;

            if (response.HasErrors && response.TotalFiles == 0 && response.ProcessedFiles == 0)
            {
                PrintValidationErrors(response.Errors);
                return;
            }

            if (response.HasWarnings && !request.ProceedOnWarnings)
            {
                response = await HandleWarningsAsync(
                    response, request, operationName, cts.Token);

                if (response is null)
                    return;
            }

            ResultRenderer.Print(response, operationName);
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine("[yellow]Operation cancelled by user.[/]");
        }
        catch (EncryptionException ex)
        {
            AnsiConsole.MarkupLine($"[red]? {Markup.Escape(ex.Code.ToString())}: {Markup.Escape(ex.Message)}[/]");
        }
        catch (ValidationException ex)
        {
            AnsiConsole.MarkupLine($"[red]? Validation error: {Markup.Escape(ex.Message)}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]? Unexpected error: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private async Task<FileCryptResult?> HandleWarningsAsync(
        FileCryptResult response,
        FileCryptRequest request,
        string operationName,
        CancellationToken cancellationToken)
    {
        AnsiConsole.MarkupLine("[yellow]? Warnings:[/]");
        foreach (string warning in response.Warnings)
        {
            AnsiConsole.MarkupLine($"  [yellow]• {Markup.Escape(warning)}[/]");
        }
        AnsiConsole.WriteLine();

        if (!AnsiConsole.Confirm($"[yellow]Continue with {operationName.ToLower()} despite warnings?[/]"))
        {
            AnsiConsole.MarkupLine("[grey]Operation cancelled.[/]");
            return null;
        }

        FileCryptRequest proceedRequest = request with { ProceedOnWarnings = true };

        Result<FileCryptResult> result = await ProgressRunner.RunAsync(
            orchestrator, proceedRequest, operationName, cancellationToken);

        if (!result.IsSuccess)
        {
            PrintFailure(operationName, result.Errors);
            return null;
        }

        return result.Value;
    }

    private static string PromptSourcePath() =>
        AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Source path[/] (file or directory):")
                .ValidationErrorMessage("[red]Path cannot be empty[/]")
                .Validate(p =>
                    !string.IsNullOrWhiteSpace(p) && (File.Exists(p) || Directory.Exists(p))
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Path does not exist[/]")
                )
        );

    private static string PromptDestinationPath() =>
        AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Destination path[/]:")
                .ValidationErrorMessage("[red]Path cannot be empty[/]")
                .Validate(p =>
                    !string.IsNullOrWhiteSpace(p)
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Please enter a destination path[/]")
                )
        );

    private static (string Password, string ConfirmPassword) PromptPasswords()
    {
        string password = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Password[/]:")
                .Secret()
                .ValidationErrorMessage("[red]Password cannot be empty[/]")
                .Validate(p =>
                    !string.IsNullOrWhiteSpace(p)
                        ? ValidationResult.Success()
                        : ValidationResult.Error("[red]Password cannot be empty[/]")
                )
        );

        string confirmPassword = AnsiConsole.Prompt(
            new TextPrompt<string>("[green]Confirm password[/]:")
                .Secret()
        );

        return (password, confirmPassword);
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
            _ => "white"
        };
        AnsiConsole.MarkupLine($"  Password strength: [{strengthColor}]{strength.Strength}[/] — {Markup.Escape(strength.Description)}");
        AnsiConsole.WriteLine();
    }

    private static T PromptStrategy<T>(string title, IReadOnlyList<T> strategies, Func<T, string> converter)
        where T : class =>
        AnsiConsole.Prompt(
            new SelectionPrompt<T>()
                .Title($"[green]{title}[/]")
                .HighlightStyle(Style.Parse("bold cyan"))
                .UseConverter(converter)
                .AddChoices(strategies)
        );

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
            .Title($"[bold cyan]{operationName} Summary[/]")
            .AddColumn(new TableColumn("[bold]Setting[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Value[/]").LeftAligned());

        summaryTable.AddRow("Source", Markup.Escape(sourcePath));
        summaryTable.AddRow("Destination", Markup.Escape(destinationPath));
        summaryTable.AddRow("Encryption", Markup.Escape(encryption.DisplayName));
        summaryTable.AddRow("Key Derivation", Markup.Escape(kdf.DisplayName));
        summaryTable.AddRow("Name Obfuscation", Markup.Escape(obfuscation.DisplayName));
        summaryTable.AddRow("Compression", Markup.Escape(compression.DisplayName));
        AnsiConsole.Write(summaryTable);
        AnsiConsole.WriteLine();
    }

    private static void PrintFailure(string operationName, string[] errors) =>
        AnsiConsole.MarkupLine(
            $"[red]? {operationName} failed: {Markup.Escape(string.Join(", ", errors))}[/]");

    private static void PrintValidationErrors(IReadOnlyList<string> errors)
    {
        AnsiConsole.MarkupLine("[red]Validation errors:[/]");
        foreach (string error in errors)
        {
            AnsiConsole.MarkupLine($"  [red]• {Markup.Escape(error)}[/]");
        }
    }
}
