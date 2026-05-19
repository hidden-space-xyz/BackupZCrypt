namespace BackupZCrypt.Domain.Constants;

public static class BackupConstants
{
    public const string AppFileExtension = ".bzc";
    public const string ManifestFileName = "manifest" + AppFileExtension;
    public static ReadOnlyMemory<byte> CompressedFileMagic { get; } = "BZC"u8.ToArray();
    /// <summary>
    /// 3 magic + 1 compression mode
    /// </summary>
    public const int CompressedFileHeaderSize = 4;

    public const string ChunksDirectoryName = "chunks";
    public const int ChunkTargetSize = 1 * 1024 * 1024;      // 1 MB
    public const int ChunkMinSize = 256 * 1024;               // 256 KB
    public const int ChunkMaxSize = 4 * 1024 * 1024;          // 4 MB
    public const int ManifestVersion1 = 1;
    public const int ManifestVersion2 = 2;
}
