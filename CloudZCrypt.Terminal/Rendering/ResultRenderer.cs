using CloudZCrypt.Application.Utilities.Formatters;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;
using CloudZCrypt.Terminal.Resources;
using Spectre.Console;

namespace CloudZCrypt.Terminal.Rendering;

internal static class ResultRenderer
{
    public static void Print(FileCryptResult response, string operationName)
    {
        AnsiConsole.WriteLine();

        string statusIcon = response.IsSuccess ? "✅" : (response.IsPartialSuccess ? "⚠" : "❌");
        string statusColor = response.IsSuccess
            ? "green"
            : (response.IsPartialSuccess ? "yellow" : "red");
        string statusText = response.IsSuccess
            ? Messages.CompletedSuccessfully
            : (response.IsPartialSuccess ? Messages.PartiallyCompleted : Messages.Failed);

        AnsiConsole.MarkupLine(
            $"[{statusColor} bold]{statusIcon} {operationName}: {statusText}[/]"
        );
        AnsiConsole.WriteLine();

        Table resultTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title($"[bold cyan]{Messages.Results}[/]")
            .AddColumn(new TableColumn($"[bold]{Messages.Metric}[/]").LeftAligned())
            .AddColumn(new TableColumn($"[bold]{Messages.Value}[/]").RightAligned());

        resultTable.AddRow(Messages.FilesProcessed, $"{response.ProcessedFiles} / {response.TotalFiles}");
        resultTable.AddRow(Messages.TotalSize, ByteSizeFormatter.Format(response.TotalBytes));
        resultTable.AddRow(Messages.ElapsedTime, response.ElapsedTime.ToString(@"hh\:mm\:ss\.fff"));
        resultTable.AddRow(
            Messages.Throughput,
            $"{ByteSizeFormatter.Format((long)response.BytesPerSecond)}/s"
        );

        if (response.FailedFiles > 0)
        {
            resultTable.AddRow($"[red]{Messages.FailedFiles}[/]", $"[red]{response.FailedFiles}[/]");
        }

        AnsiConsole.Write(resultTable);

        if (response.HasErrors)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[red]{Messages.Errors}[/]");
            foreach (string error in response.Errors)
            {
                AnsiConsole.MarkupLine($"  [red]• {Markup.Escape(error)}[/]");
            }
        }

        if (response.HasWarnings)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[yellow]{Messages.WarningsLabel}[/]");
            foreach (string warning in response.Warnings)
            {
                AnsiConsole.MarkupLine($"  [yellow]• {Markup.Escape(warning)}[/]");
            }
        }
    }
}
