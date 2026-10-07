namespace BackupZCrypt.Domain.ValueObjects.FileSystem;

/// <summary>
/// The result of walking a source directory tree: the regular files to back up, the leaf folders
/// that hold none, and every entry the walk had to leave out, so a caller can report them instead
/// of silently backing up less than the user selected.
/// </summary>
/// <param name="Files">The full paths of the regular files found, in no particular order.</param>
/// <param name="EmptyDirectories">
/// The full paths of the folders below the root that contain no regular file and no subfolder that
/// was descended into, so recreating them restores every folder of the tree.
/// </param>
/// <param name="InaccessibleDirectories">The folders below the root whose contents could not be listed.</param>
/// <param name="SkippedLinks">The full paths of the symbolic links and junctions that were not followed.</param>
public sealed record class DirectoryScan(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> EmptyDirectories,
    IReadOnlyList<InaccessibleDirectory> InaccessibleDirectories,
    IReadOnlyList<string> SkippedLinks
);
