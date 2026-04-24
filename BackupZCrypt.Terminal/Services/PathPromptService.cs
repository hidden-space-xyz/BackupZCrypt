namespace BackupZCrypt.Terminal.Services;

using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Terminal.Resources;
using Spectre.Console;
using System.Security;
using System.Text;

internal sealed class PathPromptService(IRecentPathSettingsService recentPathSettingsService)
{
    private const int MaxSuggestionCount = 12;

    public async Task<string> PromptSourcePathAsync()
    {
        RecentPathSettings recentPaths = await this.GetRecentPathSettingsAsync();

        return PromptPath(
            new PathPromptDefinition(
                Messages.SourcePathPrompt,
                Messages.SourcePathHint,
                Messages.PathCannotBeEmpty,
                Messages.PathDoesNotExist,
                recentPaths.LastSourcePath,
                PathValidationMode.ExistingFileOrDirectory,
                AllowFileBrowsing: true));
    }

    public async Task<string> PromptDestinationPathAsync()
    {
        RecentPathSettings recentPaths = await this.GetRecentPathSettingsAsync();

        return PromptPath(
            new PathPromptDefinition(
                Messages.DestinationPathPrompt,
                string.Empty,
                Messages.PleaseEnterDestinationPath,
                Messages.InvalidPath,
                recentPaths.LastDestinationPath,
                PathValidationMode.AnyPath,
                AllowFileBrowsing: true));
    }

    public async Task<string> PromptUpdateSourcePathAsync()
    {
        RecentPathSettings recentPaths = await this.GetRecentPathSettingsAsync();

        return PromptPath(
            new PathPromptDefinition(
                Messages.UpdateSourcePathPrompt,
                Messages.UpdateSourcePathHint,
                Messages.PathCannotBeEmpty,
                Messages.UpdateSourceMustBeDirectory,
                recentPaths.LastSourcePath,
                PathValidationMode.ExistingDirectory,
                AllowFileBrowsing: false));
    }

    public async Task<string> PromptUpdateBackupPathAsync()
    {
        RecentPathSettings recentPaths = await this.GetRecentPathSettingsAsync();

        return PromptPath(
            new PathPromptDefinition(
                Messages.UpdateBackupPathPrompt,
                Messages.UpdateBackupPathHint,
                Messages.PathCannotBeEmpty,
                Messages.PathDoesNotExist,
                recentPaths.LastDestinationPath,
                PathValidationMode.ExistingDirectory,
                AllowFileBrowsing: false));
    }

    public async Task RememberPathsAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await recentPathSettingsService.RememberPathsAsync(
                sourcePath,
                destinationPath,
                cancellationToken);
        }
        catch (Exception ex) when (
            ex is ArgumentException
            or InvalidOperationException
            or IOException
            or NotSupportedException
            or SecurityException
            or UnauthorizedAccessException)
        {
            PrintWarning(Messages.PathHistorySaveWarningFormat, ex.Message);
        }
    }

    private static string PromptPath(PathPromptDefinition definition)
    {
        while (true)
        {
            string manualChoice = Messages.TypeOrPastePathOption;
            string? useLastChoice = string.IsNullOrWhiteSpace(definition.LastPath)
                ? null
                : string.Format(
                    Messages.UseLastPathOptionFormat,
                    Markup.Escape(TruncateMiddle(definition.LastPath, 72)));

            if (useLastChoice is null)
            {
                string? path = PromptManualPath(definition);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    return path;
                }

                continue;
            }

            List<string> choices = [manualChoice, useLastChoice];

            string selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title(
                        $"[green]{Markup.Escape(string.Format(Messages.PathSelectionPromptFormat, definition.Label))}[/]")
                    .HighlightStyle(Style.Parse("bold cyan"))
                    .PageSize(Math.Max(3, choices.Count))
                    .AddChoices(choices));

            if (selected == manualChoice)
            {
                string? path = PromptManualPath(definition);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    return path;
                }

                continue;
            }

            if (TryValidatePath(
                    definition.LastPath ?? string.Empty,
                    definition,
                    out string normalizedLastPath,
                    out string errorMessage))
            {
                return normalizedLastPath;
            }

            AnsiConsole.MarkupLine(
                $"[red]{Markup.Escape(string.Format(Messages.SavedPathUnavailableFormat, definition.Label))}[/]");
            AnsiConsole.MarkupLine($"[dim]{Markup.Escape(errorMessage)}[/]");
            AnsiConsole.WriteLine();
        }
    }

    private static string? PromptManualPath(PathPromptDefinition definition)
    {
        while (true)
        {
            AnsiConsole.MarkupLine($"[dim]{Messages.PathManualInputHelp}[/]");

            string prompt = BuildPrompt(definition);
            Console.Write(prompt);

            StringBuilder inputBuilder = new();

            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);

                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();

                    if (TryValidatePath(
                            inputBuilder.ToString(),
                            definition,
                            out string normalizedPath,
                            out string errorMessage))
                    {
                        return normalizedPath;
                    }

                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(errorMessage)}[/]");
                    AnsiConsole.WriteLine();
                    break;
                }

                if (key.Key == ConsoleKey.Escape)
                {
                    Console.WriteLine();
                    AnsiConsole.WriteLine();
                    return null;
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (inputBuilder.Length > 0)
                    {
                        inputBuilder.Length--;
                        Console.Write('\b');
                        Console.Write(' ');
                        Console.Write('\b');
                    }

                    continue;
                }

                if (key.Key == ConsoleKey.Tab)
                {
                    CompletionResult result = GetCompletionResult(
                        inputBuilder.ToString(),
                        definition.ValidationMode,
                        definition.AllowFileBrowsing);

                    if (result.Replacement.Length > inputBuilder.Length)
                    {
                        string suffix = result.Replacement[inputBuilder.Length..];
                        inputBuilder.Clear();
                        inputBuilder.Append(result.Replacement);
                        Console.Write(suffix);
                    }

                    if (result.ShouldPrintSuggestions)
                    {
                        Console.WriteLine();
                        AnsiConsole.MarkupLine(string.Join(", ", result.Suggestions));
                        Console.Write(prompt);
                        Console.Write(inputBuilder.ToString());
                    }

                    continue;
                }

                if (!char.IsControl(key.KeyChar))
                {
                    inputBuilder.Append(key.KeyChar);
                    Console.Write(key.KeyChar);
                }
            }
        }
    }

    private static CompletionResult GetCompletionResult(
        string input,
        PathValidationMode validationMode,
        bool allowFileBrowsing)
    {
        if (!TryCreateCompletionContext(input, out CompletionContext context))
        {
            return CompletionResult.None(input);
        }

        if (!Directory.Exists(context.SearchDirectory))
        {
            return CompletionResult.None(input);
        }

        List<CompletionCandidate> candidates;

        try
        {
            candidates = Directory
                .EnumerateFileSystemEntries(context.SearchDirectory)
                .Select(path => new CompletionCandidate(path, Directory.Exists(path)))
                .Where(candidate =>
                    (allowFileBrowsing || candidate.IsDirectory)
                    && candidate.Name.StartsWith(context.PartialName, StringComparison.OrdinalIgnoreCase)
                    && (validationMode != PathValidationMode.ExistingDirectory || candidate.IsDirectory))
                .OrderByDescending(candidate => candidate.IsDirectory)
                .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (
            ex is IOException
            or UnauthorizedAccessException
            or SecurityException)
        {
            return CompletionResult.None(input);
        }

        if (candidates.Count == 0)
        {
            return CompletionResult.None(input);
        }

        if (candidates.Count == 1)
        {
            string completedPath = context.ReplacementPrefix
                + candidates[0].Name
                + (candidates[0].IsDirectory ? Path.DirectorySeparatorChar : string.Empty);

            return new CompletionResult(completedPath, [], false);
        }

        string commonPrefix = GetLongestCommonPrefix(candidates.Select(candidate => candidate.Name));
        if (commonPrefix.Length > context.PartialName.Length)
        {
            return new CompletionResult(context.ReplacementPrefix + commonPrefix, [], false);
        }

        List<string> suggestions = candidates
            .Take(MaxSuggestionCount)
            .Select(candidate =>
                candidate.IsDirectory
                    ? $"[blue]{Markup.Escape(candidate.Name)}[/]"
                    : $"[grey]{Markup.Escape(candidate.Name)}[/]")
            .ToList();

        if (candidates.Count > MaxSuggestionCount)
        {
            suggestions.Add("[dim]...[/]");
        }

        return new CompletionResult(input, suggestions, true);
    }

    private static bool TryCreateCompletionContext(string input, out CompletionContext context)
    {
        string trimmedInput = input.Trim();
        int separatorIndex = trimmedInput.LastIndexOfAny(['\\', '/']);

        if (separatorIndex < 0)
        {
            context = new CompletionContext(
                Environment.CurrentDirectory,
                string.Empty,
                trimmedInput);
            return true;
        }

        string replacementPrefix = trimmedInput[..(separatorIndex + 1)];
        string partialName = trimmedInput[(separatorIndex + 1)..];

        if (!TryNormalizePath(replacementPrefix, out string searchDirectory))
        {
            context = default;
            return false;
        }

        context = new CompletionContext(searchDirectory, replacementPrefix, partialName);
        return true;
    }

    private static bool TryValidatePath(
        string rawInput,
        PathPromptDefinition definition,
        out string normalizedPath,
        out string errorMessage)
    {
        normalizedPath = string.Empty;

        string sanitizedInput = SanitizePath(rawInput);
        if (string.IsNullOrWhiteSpace(sanitizedInput))
        {
            errorMessage = definition.EmptyPathError;
            return false;
        }

        if (!TryNormalizePath(sanitizedInput, out normalizedPath))
        {
            errorMessage = definition.InvalidPathError;
            return false;
        }

        bool isValid = definition.ValidationMode switch
        {
            PathValidationMode.AnyPath => true,
            PathValidationMode.ExistingDirectory => Directory.Exists(normalizedPath),
            _ => File.Exists(normalizedPath) || Directory.Exists(normalizedPath),
        };

        if (!isValid)
        {
            normalizedPath = string.Empty;
            errorMessage = definition.InvalidPathError;
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private async Task<RecentPathSettings> GetRecentPathSettingsAsync()
    {
        try
        {
            return await recentPathSettingsService.GetOrCreateAsync();
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
            or IOException
            or NotSupportedException
            or SecurityException
            or UnauthorizedAccessException)
        {
            PrintWarning(Messages.PathHistoryLoadWarningFormat, ex.Message);
            return RecentPathSettings.Default;
        }
    }

    private static void PrintWarning(string format, string detail)
    {
        AnsiConsole.MarkupLine(
            $"[yellow]{string.Format(format, Markup.Escape(detail))}[/]");
        AnsiConsole.WriteLine();
    }

    private static bool TryNormalizePath(string path, out string normalizedPath)
    {
        try
        {
            normalizedPath = Path.GetFullPath(SanitizePath(path));
            return true;
        }
        catch (Exception ex) when (
            ex is ArgumentException
            or IOException
            or NotSupportedException
            or SecurityException
            or UnauthorizedAccessException)
        {
            normalizedPath = string.Empty;
            return false;
        }
    }

    private static string GetLongestCommonPrefix(IEnumerable<string> values)
    {
        string[] candidates = values.ToArray();
        if (candidates.Length == 0)
        {
            return string.Empty;
        }

        string prefix = candidates[0];

        foreach (string candidate in candidates.Skip(1))
        {
            int maxLength = Math.Min(prefix.Length, candidate.Length);
            var sharedLength = 0;

            while (sharedLength < maxLength
                   && char.ToUpperInvariant(prefix[sharedLength]) == char.ToUpperInvariant(candidate[sharedLength]))
            {
                sharedLength++;
            }

            prefix = prefix[..sharedLength];

            if (prefix.Length == 0)
            {
                break;
            }
        }

        return prefix;
    }

    private static string TruncateMiddle(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        int headLength = (maxLength - 3) / 2;
        int tailLength = maxLength - headLength - 3;

        return string.Concat(
            value.AsSpan(0, headLength),
            "...",
            value.AsSpan(value.Length - tailLength));
    }

    private static string BuildPrompt(PathPromptDefinition definition) =>
        string.IsNullOrWhiteSpace(definition.Hint)
            ? $"{definition.Label}: "
            : $"{definition.Label} {definition.Hint} ";

    private static string SanitizePath(string rawInput)
    {
        string sanitized = rawInput.Trim();

        if (sanitized.Length >= 2
            && sanitized[0] == '"'
            && sanitized[^1] == '"')
        {
            return sanitized[1..^1];
        }

        return sanitized;
    }

    private readonly record struct PathPromptDefinition(
        string Label,
        string Hint,
        string EmptyPathError,
        string InvalidPathError,
        string? LastPath,
        PathValidationMode ValidationMode,
        bool AllowFileBrowsing);

    private readonly record struct CompletionContext(
        string SearchDirectory,
        string ReplacementPrefix,
        string PartialName);

    private sealed record CompletionCandidate(string Path, bool IsDirectory)
    {
        public string Name { get; } = System.IO.Path.GetFileName(Path);
    }

    private sealed record CompletionResult(
        string Replacement,
        IReadOnlyList<string> Suggestions,
        bool ShouldPrintSuggestions)
    {
        public static CompletionResult None(string replacement) => new(replacement, [], false);
    }

    private enum PathValidationMode
    {
        ExistingFileOrDirectory = 0,
        ExistingDirectory = 1,
        AnyPath = 2,
    }
}
