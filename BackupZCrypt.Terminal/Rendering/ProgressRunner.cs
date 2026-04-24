namespace BackupZCrypt.Terminal.Rendering;

using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Terminal.Resources;
using Spectre.Console;

internal static class ProgressRunner
{
    public static async Task<Result<BackupResult>> RunAsync(
        IBackupOrchestrator orchestrator,
        BackupRequest request,
        string operationIngName,
        CancellationToken cancellationToken)
    {
        Result<BackupResult>? result = null;

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
                var task = ctx.AddTask(
                    $"[cyan]{string.Format(Messages.OperationIngFormat, operationIngName)}[/]",
                    maxValue: 100);

                Progress<BackupStatus> progress = new(update =>
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
