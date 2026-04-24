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
}
