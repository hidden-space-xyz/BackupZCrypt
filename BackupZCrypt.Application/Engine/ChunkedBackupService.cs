using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Application.Engine;

/// <summary>
/// The chunk-based backup engine behind <see cref="IChunkedBackupService"/>. Each operation is owned
/// by one part: <see cref="BackupWriter"/> creates and updates archives, <see cref="BackupRestorer"/>
/// restores and verifies them, and <see cref="BackupPreviewer"/> compares an update or restore with
/// the archive before it runs. <see cref="BackupOpener"/> opens the archive for all three, while
/// <see cref="ChunkStore"/> backs the writer and <see cref="ChunkReader"/> the restorer.
/// </summary>
/// <param name="writer">The part that creates and updates archives.</param>
/// <param name="restorer">The part that restores and verifies archives.</param>
/// <param name="previewer">The part that previews updates and restores.</param>
internal sealed class ChunkedBackupService(
    BackupWriter writer,
    BackupRestorer restorer,
    BackupPreviewer previewer
) : IChunkedBackupService
{
    /// <inheritdoc/>
    public Task<Result<BackupResult>> CreateAsync(
        string sourcePath,
        string destinationPath,
        CreateBackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        return writer.CreateAsync(sourcePath, destinationPath, request, progress, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<BackupResult>> UpdateAsync(
        string sourcePath,
        string destinationPath,
        UpdateBackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        return writer.UpdateAsync(sourcePath, destinationPath, request, progress, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<BackupResult>> RestoreAsync(
        string sourcePath,
        string destinationPath,
        RestoreBackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        return restorer.RestoreAsync(sourcePath, destinationPath, request, progress, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<BackupResult>> VerifyAsync(
        string sourcePath,
        VerifyBackupRequest request,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        return restorer.VerifyAsync(sourcePath, request, progress, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<BackupPreview>> PreviewUpdateAsync(
        string sourcePath,
        string backupPath,
        string password,
        CancellationToken cancellationToken
    )
    {
        return previewer.PreviewUpdateAsync(sourcePath, backupPath, password, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<Result<BackupPreview>> PreviewRestoreAsync(
        string backupPath,
        string password,
        CancellationToken cancellationToken
    )
    {
        return previewer.PreviewRestoreAsync(backupPath, password, cancellationToken);
    }
}
