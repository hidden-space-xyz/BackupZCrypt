namespace BackupZCrypt.Application.Common;

/// <summary>
/// What an update did to the files the backup already held: how many it carried over without
/// rewriting them and how many it removed because they are no longer in the source.
/// </summary>
public sealed record class UpdateSummary
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateSummary"/> class.
    /// </summary>
    /// <param name="unchangedFiles">The number of files the update carried over without rewriting them.</param>
    /// <param name="removedFiles">The number of files the update removed from the backup.</param>
    /// <exception cref="ArgumentOutOfRangeException">A file count is negative.</exception>
    public UpdateSummary(int unchangedFiles, int removedFiles)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(unchangedFiles);
        ArgumentOutOfRangeException.ThrowIfNegative(removedFiles);

        this.UnchangedFiles = unchangedFiles;
        this.RemovedFiles = removedFiles;
    }

    /// <summary>
    /// Gets the number of files the update carried over from the previous backup without rewriting them.
    /// </summary>
    public int UnchangedFiles { get; }

    /// <summary>
    /// Gets the number of files the update removed from the backup because they are no longer in the source.
    /// </summary>
    public int RemovedFiles { get; }
}
