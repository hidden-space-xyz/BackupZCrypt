using CloudZCrypt.Application.Orchestrators.Interfaces;
using CloudZCrypt.Application.ValueObjects;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;
using Spectre.Console;

namespace CloudZCrypt.Terminal.Rendering;

internal static class ProgressRunner
{
    public static async Task<Result<FileCryptResult>> RunAsync(
        IFileCryptOrchestrator orchestrator,
        FileCryptRequest request,
        string operationName,
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
                ProgressTask task = ctx.AddTask($"[cyan]{operationName}ing…[/]", maxValue: 100);

                Progress<FileCryptStatus> progress = new(update =>
                {
                    double pct =
                        update.TotalBytes > 0
                            ? (double)update.ProcessedBytes / update.TotalBytes * 100
                            : 100;
                    task.Value = pct;
                    task.Description =
                        $"[cyan]{operationName}ing[/] {update.ProcessedFiles}/{update.TotalFiles} files";
                });

                result = await orchestrator.ExecuteAsync(request, progress, cancellationToken);
            });

        return result!;
    }
}
