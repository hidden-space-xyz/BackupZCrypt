using CloudZCrypt.Application.Orchestrators.Interfaces;
using CloudZCrypt.Application.ValueObjects;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;
using CloudZCrypt.Terminal.Resources;
using Spectre.Console;

namespace CloudZCrypt.Terminal.Rendering;

internal static class ProgressRunner
{
    public static async Task<Result<FileCryptResult>> RunAsync(
        IFileCryptOrchestrator orchestrator,
        FileCryptRequest request,
        string operationName,
        string operationIngName,
        CancellationToken cancellationToken
    )
    {
        Result<FileCryptResult>? result = null;

        await AnsiConsole
            .Progress()
            .AutoClear(false)
            .HideCompleted(false)
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new RemainingTimeColumn(),
                new SpinnerColumn()
            )
            .StartAsync(async ctx =>
            {
                ProgressTask task = ctx.AddTask(
                    $"[cyan]{string.Format(Messages.OperationIngFormat, operationIngName)}[/]",
                    maxValue: 100
                );

                Progress<FileCryptStatus> progress = new(update =>
                {
                    double pct =
                        update.TotalBytes > 0
                            ? (double)update.ProcessedBytes / update.TotalBytes * 100
                            : 100;
                    task.Value = pct;
                    task.Description =
                        $"[cyan]{string.Format(Messages.OperationIngFilesFormat, operationIngName, update.ProcessedFiles, update.TotalFiles)}[/]";
                });

                result = await orchestrator.ExecuteAsync(request, progress, cancellationToken);
            });

        return result!;
    }
}
