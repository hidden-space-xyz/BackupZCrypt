namespace BackupZCrypt.Domain.Constants;

public static class FileCryptConstants
{
    public const string AppFileExtension = ".bzc";
    public const string ManifestFileName = "manifest" + AppFileExtension;
    public static readonly byte[] CompressedFileMagic = "BZC"u8.ToArray();
    public const int CompressedFileHeaderSize = 4; // 3 magic + 1 compression mode
}
