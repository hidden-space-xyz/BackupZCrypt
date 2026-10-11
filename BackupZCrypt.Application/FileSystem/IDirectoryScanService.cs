namespace BackupZCrypt.Application.FileSystem;

/// <summary>
/// Walks the source tree a backup reads, deciding which entries are regular files, which folders
/// are empty, and which entries have to be left out.
/// </summary>
public interface IDirectoryScanService
{
    /// <summary>
    /// Walks a source directory tree without following symbolic links or junctions, collecting the
    /// regular files, the empty leaf folders, and every folder or link that had to be left out.
    /// </summary>
    /// <param name="directoryPath">The root directory to walk.</param>
    /// <param name="cancellationToken">A token to cancel the walk.</param>
    /// <returns>The files and folders found and the entries that were skipped.</returns>
    /// <exception cref="UnauthorizedAccessException">The root directory itself cannot be listed.</exception>
    /// <exception cref="IOException">The root directory cannot be opened.</exception>
    public Task<DirectoryScan> ScanDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default
    );
}
