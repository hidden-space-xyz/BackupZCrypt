namespace BackupZCrypt.Terminal.Rendering;

using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.ValueObjects.FileCrypt;
using BackupZCrypt.Terminal.Resources;
using Spectre.Console;

internal static class ProgressRunner
{
    public static async Task<Result<FileCryptResult>> RunAsync(
        IFileCryptOrchestrator orchestrator,
        FileCryptRequest request,
        string operationIngName,
        CancellationToken cancellationToken)
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
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                ProgressTask task = ctx.AddTask(
                    $"[cyan]{string.Format(Messages.OperationIngFormat, operationIngName)}[/]",
                    maxValue: 100);

                Progress<FileCryptStatus> progress = new(update =>
                {
                    task.Value = (update.TotalBytes > 0
                            ? (double)update.ProcessedBytes / update.TotalBytes * 100
                            : 100);

                    task.Description =
                        $"[cyan]{string.Format(Messages.OperationIngFilesFormat, operationIngName, update.ProcessedFiles, update.TotalFiles)}[/]";
                });

                result = await orchestrator.ExecuteAsync(request, progress, cancellationToken);
            });

        return result!;
    }
}
