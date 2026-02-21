namespace CloudZCrypt.Terminal.Rendering;

using CloudZCrypt.Application.Utilities.Formatters;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;
using CloudZCrypt.Terminal.Resources;
using Spectre.Console;

internal static class ResultRenderer
{
    public static void Print(FileCryptResult response, string operationName)
    {
        AnsiConsole.WriteLine();

        string statusIcon;
        if (response.IsSuccess)
        {
            statusIcon = "✅";
        }
        else if (response.IsPartialSuccess)
        {
            statusIcon = "⚠";
        }
        else
        {
            statusIcon = "❌";
        }

        string statusColor;
        if (response.IsSuccess)
        {
            statusColor = "green";
        }
        else if (response.IsPartialSuccess)
        {
            statusColor = "yellow";
        }
        else
        {
            statusColor = "red";
        }

        string statusText;
        if (response.IsSuccess)
        {
            statusText = Messages.CompletedSuccessfully;
        }
        else if (response.IsPartialSuccess)
        {
            statusText = Messages.PartiallyCompleted;
        }
        else
        {
            statusText = Messages.Failed;
        }

        AnsiConsole.MarkupLine(
            $"[{statusColor} bold]{statusIcon} {operationName}: {statusText}[/]");
        AnsiConsole.WriteLine();

        Table resultTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title($"[bold cyan]{Messages.Results}[/]")
            .AddColumn(new TableColumn($"[bold]{Messages.Metric}[/]").LeftAligned())
            .AddColumn(new TableColumn($"[bold]{Messages.Value}[/]").RightAligned());

        resultTable.AddRow(
            Messages.FilesProcessed,
            $"{response.ProcessedFiles} / {response.TotalFiles}");
        resultTable.AddRow(Messages.TotalSize, ByteSizeFormatter.Format(response.TotalBytes));
        resultTable.AddRow(Messages.ElapsedTime, response.ElapsedTime.ToString(@"hh\:mm\:ss\.fff"));
        resultTable.AddRow(
            Messages.Throughput,
            $"{ByteSizeFormatter.Format((long)response.BytesPerSecond)}/s");

        if (response.FailedFiles > 0)
        {
            resultTable.AddRow(
                $"[red]{Messages.FailedFiles}[/]",
                $"[red]{response.FailedFiles}[/]");
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
