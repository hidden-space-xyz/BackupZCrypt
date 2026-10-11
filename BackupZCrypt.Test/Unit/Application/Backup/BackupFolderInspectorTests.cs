using BackupZCrypt.Application.Backup;
using BackupZCrypt.Application.FileSystem;
using BackupZCrypt.Domain.Archive;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Application.Backup;

/// <summary>
/// Unit tests for <see cref="BackupFolderInspector"/>, which decides whether a create may write into
/// an existing folder because it holds nothing but a backup this program wrote.
/// </summary>
/// <remarks>
/// A wrong yes lets a create replace, and later prune, a folder holding user data; every case where a
/// name only looks like a backup entry therefore has to answer no.
/// </remarks>
public sealed class BackupFolderInspectorTests
{
    /// <summary>
    /// The folder under inspection; nothing is read from disk.
    /// </summary>
    private static readonly string Folder = Path.Join(Path.GetTempPath(), "bzc-inspector");

    /// <summary>
    /// The chunks directory of <see cref="Folder"/>.
    /// </summary>
    private static readonly string ChunksFolder = Path.Join(Folder, BackupConstants.ChunksDirectoryName);

    /// <summary>
    /// A valid chunk file name.
    /// </summary>
    private static readonly string ChunkName = new string('b', 64) + BackupConstants.AppFileExtension;

    /// <summary>
    /// The file-system port the inspector reads the folder through.
    /// </summary>
    private readonly IFileOperationsService files = Substitute.For<IFileOperationsService>();

    [Fact]
    internal void EmptyFolder_HoldsOnlyBackupFilesAndNoManifest()
    {
        this.Entries(Folder);

        Assert.True(this.files.ContainsOnlyBackupFiles(Folder, out var containsManifest));
        Assert.False(containsManifest);
    }

    [Fact]
    internal void CompleteBackup_HoldsOnlyBackupFilesAndReportsItsManifest()
    {
        var temporary = BackupLayout.NewTemporaryFileName();
        this.Entries(
            Folder,
            BackupConstants.ManifestFileName,
            BackupConstants.ChunksDirectoryName,
            BackupConstants.LockFileName,
            temporary
        );
        this.MarkFile(BackupConstants.ManifestFileName);
        this.MarkFile(BackupConstants.LockFileName);
        this.MarkFile(temporary);
        this.ChunksDirectory(ChunkName, ChunkName + BackupConstants.QuarantineExtension, BackupLayout.NewTemporaryFileName());

        Assert.True(this.files.ContainsOnlyBackupFiles(Folder, out var containsManifest));
        Assert.True(containsManifest);
    }

    [Fact]
    internal void NamesMatchingInADifferentCase_StillCount()
    {
        this.Entries(Folder, BackupConstants.ManifestFileName.ToUpperInvariant());
        this.MarkFile(BackupConstants.ManifestFileName.ToUpperInvariant());

        Assert.True(this.files.ContainsOnlyBackupFiles(Folder, out var containsManifest));
        Assert.True(containsManifest);
    }

    [Fact]
    internal void ForeignFile_IsUserData()
    {
        this.Entries(Folder, BackupConstants.ManifestFileName, "holiday.jpg");
        this.MarkFile(BackupConstants.ManifestFileName);
        this.MarkFile("holiday.jpg");

        Assert.False(this.files.ContainsOnlyBackupFiles(Folder, out _));
    }

    [Theory]
    [InlineData(BackupConstants.ManifestFileName)]
    [InlineData(BackupConstants.LockFileName)]
    internal void BackupFileNameThatIsAFolder_IsUserData(string name)
    {
        this.Entries(Folder, name);
        _ = this.files.DirectoryExists(Path.Join(Folder, name)).Returns(true);

        Assert.False(this.files.ContainsOnlyBackupFiles(Folder, out var containsManifest));
        Assert.False(containsManifest);
    }

    [Fact]
    internal void ChunksNameThatIsAFile_IsUserData()
    {
        this.Entries(Folder, BackupConstants.ChunksDirectoryName);
        this.MarkFile(BackupConstants.ChunksDirectoryName);

        Assert.False(this.files.ContainsOnlyBackupFiles(Folder, out _));
    }

    [Fact]
    internal void ChunksFolderReachedThroughALink_IsUserData()
    {
        this.Entries(Folder, BackupConstants.ChunksDirectoryName);
        this.ChunksDirectory(ChunkName);
        _ = this.files.IsReparsePoint(ChunksFolder).Returns(true);

        Assert.False(this.files.ContainsOnlyBackupFiles(Folder, out _));
    }

    [Fact]
    internal void ChunksFolderHoldingAForeignFile_IsUserData()
    {
        this.Entries(Folder, BackupConstants.ChunksDirectoryName);
        this.ChunksDirectory(ChunkName, "notes.txt");

        Assert.False(this.files.ContainsOnlyBackupFiles(Folder, out _));
    }

    /// <summary>
    /// Makes the port list the given names as the entries of a folder.
    /// </summary>
    /// <param name="folder">The folder listed.</param>
    /// <param name="names">The entry names.</param>
    private void Entries(string folder, params string[] names)
    {
        _ = this.files.GetDirectoryEntryNames(folder).Returns(names);
    }

    /// <summary>
    /// Makes the port report an entry of <see cref="Folder"/> as an existing file.
    /// </summary>
    /// <param name="name">The entry name.</param>
    private void MarkFile(string name)
    {
        _ = this.files.FileExists(Path.Join(Folder, name)).Returns(true);
    }

    /// <summary>
    /// Makes the port report <see cref="ChunksFolder"/> as an existing, ordinary folder holding the
    /// given entries.
    /// </summary>
    /// <param name="names">The entry names inside the chunks folder.</param>
    private void ChunksDirectory(params string[] names)
    {
        _ = this.files.DirectoryExists(ChunksFolder).Returns(true);
        this.Entries(ChunksFolder, names);
    }
}
