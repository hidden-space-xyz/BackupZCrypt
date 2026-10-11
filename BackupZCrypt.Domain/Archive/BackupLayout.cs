using System.Security.Cryptography;

namespace BackupZCrypt.Domain.Archive;

/// <summary>
/// Recognizes the names of the files and folders this program writes into a backup folder, so cleanup
/// only ever touches its own files and a create can tell a folder holding a backup from one holding
/// user data.
/// </summary>
public static class BackupLayout
{
    /// <summary>
    /// The number of hexadecimal characters in a chunk file name: the HMAC-SHA256 of the chunk hash.
    /// </summary>
    private const int ChunkNameHexLength = 64;

    /// <summary>
    /// The number of hexadecimal characters in the random part of a temporary file name.
    /// </summary>
    private const int TemporaryNameHexLength = 24;

    /// <summary>
    /// Resolves the full path of a chunk inside a backup's chunks directory, under the keyed name
    /// <see cref="ChunkCrypto.ComputeChunkFileName"/> gives it.
    /// </summary>
    /// <param name="chunksDirectory">The directory encrypted chunk files live in.</param>
    /// <param name="namingKey">The chunk-naming sub-key.</param>
    /// <param name="chunkHash">The SHA-256 content hash of the chunk.</param>
    /// <returns>The path of the chunk file.</returns>
    public static string ChunkFilePath(string chunksDirectory, byte[] namingKey, byte[] chunkHash)
    {
        return Path.Join(chunksDirectory, ChunkCrypto.ComputeChunkFileName(namingKey, chunkHash));
    }

    /// <summary>
    /// Determines whether a name is that of a stored chunk file.
    /// </summary>
    /// <param name="fileName">The file name to test.</param>
    /// <returns><see langword="true"/> for a chunk file name.</returns>
    public static bool IsChunkFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        return fileName.Length == ChunkNameHexLength + BackupConstants.AppFileExtension.Length
            && fileName.EndsWith(BackupConstants.AppFileExtension, StringComparison.OrdinalIgnoreCase)
            && IsHex(fileName.AsSpan(0, ChunkNameHexLength));
    }

    /// <summary>
    /// Determines whether a name is that of a chunk file verification set aside as damaged.
    /// </summary>
    /// <param name="fileName">The file name to test.</param>
    /// <returns><see langword="true"/> for a quarantined chunk file name.</returns>
    public static bool IsQuarantinedChunkFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        return fileName.EndsWith(BackupConstants.QuarantineExtension, StringComparison.OrdinalIgnoreCase)
            && IsChunkFileName(fileName[..^BackupConstants.QuarantineExtension.Length]);
    }

    /// <summary>
    /// Determines whether a name is that of a temporary file written before an atomic rename.
    /// </summary>
    /// <param name="fileName">The file name to test.</param>
    /// <returns><see langword="true"/> for a temporary file name.</returns>
    public static bool IsTemporaryFileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        return fileName.Length == 1 + TemporaryNameHexLength + BackupConstants.TemporaryFileExtension.Length
            && fileName[0] is '.'
            && fileName.EndsWith(BackupConstants.TemporaryFileExtension, StringComparison.OrdinalIgnoreCase)
            && IsHex(fileName.AsSpan(1, TemporaryNameHexLength));
    }

    /// <summary>
    /// Creates a fresh random name of the shape <see cref="IsTemporaryFileName"/> recognizes, for a
    /// file written beside its target before an atomic rename.
    /// </summary>
    /// <returns>The temporary file name, without a directory.</returns>
    public static string NewTemporaryFileName()
    {
        var randomPart = RandomNumberGenerator.GetBytes(TemporaryNameHexLength / 2);

        try
        {
            return "." + Convert.ToHexStringLower(randomPart) + BackupConstants.TemporaryFileExtension;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(randomPart);
        }
    }

    /// <summary>
    /// Determines whether every character is a hexadecimal digit.
    /// </summary>
    /// <param name="value">The characters to test.</param>
    /// <returns><see langword="true"/> when every character is hexadecimal.</returns>
    private static bool IsHex(ReadOnlySpan<char> value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
