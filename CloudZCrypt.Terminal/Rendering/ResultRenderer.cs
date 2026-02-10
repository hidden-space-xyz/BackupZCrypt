using CloudZCrypt.Application.Utilities.Formatters;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;
using Spectre.Console;

namespace CloudZCrypt.Terminal.Rendering;

internal static class ResultRenderer
{
    public static void Print(FileCryptResult response, string operationName)
    {
        AnsiConsole.WriteLine();

        string statusIcon = response.IsSuccess ? "?" : (response.IsPartialSuccess ? "?" : "?");
        string statusColor = response.IsSuccess ? "green" : (response.IsPartialSuccess ? "yellow" : "red");
        string statusText = response.IsSuccess
            ? "Completed successfully"
            : (response.IsPartialSuccess ? "Partially completed" : "Failed");

        AnsiConsole.MarkupLine($"[{statusColor} bold]{statusIcon} {operationName}: {statusText}[/]");
        AnsiConsole.WriteLine();

        Table resultTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title("[bold cyan]Results[/]")
            .AddColumn(new TableColumn("[bold]Metric[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Value[/]").RightAligned());

        resultTable.AddRow("Files processed", $"{response.ProcessedFiles} / {response.TotalFiles}");
        resultTable.AddRow("Total size", ByteSizeFormatter.Format(response.TotalBytes));
        resultTable.AddRow("Elapsed time", response.ElapsedTime.ToString(@"hh\:mm\:ss\.fff"));
        resultTable.AddRow("Throughput", $"{ByteSizeFormatter.Format((long)response.BytesPerSecond)}/s");

        if (response.FailedFiles > 0)
        {
            resultTable.AddRow("[red]Failed files[/]", $"[red]{response.FailedFiles}[/]");
        }

        AnsiConsole.Write(resultTable);

        if (response.HasErrors)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[red]Errors:[/]");
            foreach (string error in response.Errors)
            {
                AnsiConsole.MarkupLine($"  [red]• {Markup.Escape(error)}[/]");
            }
        }

        if (response.HasWarnings)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Warnings:[/]");
            foreach (string warning in response.Warnings)
            {
                AnsiConsole.MarkupLine($"  [yellow]• {Markup.Escape(warning)}[/]");
            }
        }
    }
}
