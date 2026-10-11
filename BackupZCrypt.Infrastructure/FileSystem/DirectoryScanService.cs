using System.IO.Enumeration;

using BackupZCrypt.Application.FileSystem;

namespace BackupZCrypt.Infrastructure.FileSystem;

/// <summary>
/// File-system implementation of <see cref="IDirectoryScanService"/> backed by <see cref="System.IO"/>.
/// </summary>
internal sealed class DirectoryScanService : IDirectoryScanService
{
    /// <summary>
    /// Walks the tree under <paramref name="directoryPath"/> on a background thread, one folder at a
    /// time, so a folder that cannot be listed is recorded and skipped instead of hiding the rest of
    /// the tree or failing the whole walk.
    /// </summary>
    /// <remarks>
    /// Only symbolic links and junctions are left out. Other reparse points — cloud placeholders,
    /// deduplicated or compacted files — hold the file's own data and are walked like any other entry.
    /// </remarks>
    /// <param name="directoryPath">The root directory to walk.</param>
    /// <param name="cancellationToken">A token to cancel the walk.</param>
    /// <returns>The files and folders found and the entries that were skipped.</returns>
    public Task<DirectoryScan> ScanDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default
    )
    {
        return Task.Run(() => ScanDirectory(directoryPath, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Walks the tree depth-first with an explicit stack, listing one folder at a time.
    /// </summary>
    /// <param name="root">The root directory to walk.</param>
    /// <param name="cancellationToken">A token to cancel the walk.</param>
    /// <returns>The files and folders found and the entries that were skipped.</returns>
    private static DirectoryScan ScanDirectory(string root, CancellationToken cancellationToken)
    {
        List<string> files = [];
        List<string> emptyDirectories = [];
        List<InaccessibleDirectory> inaccessibleDirectories = [];
        List<string> skippedLinks = [];
        Stack<string> pending = new();

        AddEntries(root, ListDirectory(root), isRoot: true);

        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<ScannedEntry> entries;
            try
            {
                entries = ListDirectory(directory);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                inaccessibleDirectories.Add(new InaccessibleDirectory(directory, exception));
                continue;
            }

            AddEntries(directory, entries, isRoot: false);
        }

        return new DirectoryScan(files, emptyDirectories, inaccessibleDirectories, skippedLinks);

        void AddEntries(string directory, List<ScannedEntry> entries, bool isRoot)
        {
            var keptEntries = 0;

            foreach (var entry in entries)
            {
                if (entry.IsLink)
                {
                    skippedLinks.Add(entry.FullPath);
                }
                else if (entry.IsDirectory)
                {
                    pending.Push(entry.FullPath);
                    keptEntries++;
                }
                else
                {
                    files.Add(entry.FullPath);
                    keptEntries++;
                }
            }

            if (!isRoot && keptEntries is 0)
            {
                emptyDirectories.Add(directory);
            }
        }
    }

    /// <summary>
    /// Lists the immediate entries of one folder, surfacing any failure to open or read it.
    /// </summary>
    /// <param name="directory">The folder to list.</param>
    /// <returns>The folder's entries.</returns>
    private static List<ScannedEntry> ListDirectory(string directory)
    {
        FileSystemEnumerable<ScannedEntry> enumerable = new(
            directory,
            static (ref entry) =>
            {
                var fullPath = entry.ToFullPath();
                var isLink =
                    entry.Attributes.HasFlag(FileAttributes.ReparsePoint) && IsLink(fullPath, entry.IsDirectory);

                return new ScannedEntry(fullPath, entry.IsDirectory, isLink);
            },
            new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.None,
                ReturnSpecialDirectories = false,
            }
        );

        return [.. enumerable];
    }

    /// <summary>
    /// Determines whether a reparse point is a symbolic link or junction rather than a file whose data
    /// merely lives behind a filter, such as a cloud placeholder.
    /// </summary>
    /// <param name="fullPath">The full path of the reparse point.</param>
    /// <param name="isDirectory">Whether the entry is a folder.</param>
    /// <returns><see langword="true"/> when the entry links to another location.</returns>
    private static bool IsLink(string fullPath, bool isDirectory)
    {
        try
        {
            FileSystemInfo info = isDirectory ? new DirectoryInfo(fullPath) : new FileInfo(fullPath);
            return info.LinkTarget is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>
    /// One entry found while walking a folder.
    /// </summary>
    /// <param name="FullPath">The entry's full path.</param>
    /// <param name="IsDirectory">Whether the entry is a folder.</param>
    /// <param name="IsLink">Whether the entry is a symbolic link or junction.</param>
    private readonly record struct ScannedEntry(string FullPath, bool IsDirectory, bool IsLink);
}
