using System.Security.Cryptography;

using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Application.Services;

internal sealed partial class ChunkedBackupService
{
    /// <summary>
    /// Derives the master key from the password and salt, then expands it into the purpose-bound
    /// sub-keys used for chunk encryption, nonce derivation, chunk naming, and the manifest.
    /// </summary>
    /// <remarks>
    /// The master key is zeroed if sub-key expansion fails, so no key material survives an error.
    /// </remarks>
    /// <param name="password">The user's password.</param>
    /// <param name="salt">The master salt generated for or read from the backup.</param>
    /// <param name="kdf">The key derivation algorithm recorded in the manifest preamble.</param>
    /// <returns>The derived key set; disposing it wipes every key it holds.</returns>
    private DerivedKeySet DeriveKeySet(string password, byte[] salt, KeyDerivationAlgorithm kdf)
    {
        var strategy = keyDerivationServiceFactory.Create(kdf);
        var masterKey = strategy.DeriveKey(password, salt, EncryptionConstants.KeySize);

        byte[]? chunkEncryptionKey = null;
        byte[]? chunkNonceKey = null;
        byte[]? namingKey = null;
        byte[]? manifestEncryptionKey = null;

        try
        {
            chunkEncryptionKey = DeriveSubKey(masterKey, "chunk-encryption"u8);
            chunkNonceKey = DeriveSubKey(masterKey, "chunk-nonce"u8);
            namingKey = DeriveSubKey(masterKey, "chunk-naming"u8);
            manifestEncryptionKey = DeriveSubKey(masterKey, "manifest-encryption"u8);

            return new DerivedKeySet(
                masterKey,
                chunkEncryptionKey,
                chunkNonceKey,
                namingKey,
                manifestEncryptionKey
            );
        }
        catch
        {
            CryptographicOperations.ZeroMemory(masterKey);
            ZeroKeyIfCreated(chunkEncryptionKey);
            ZeroKeyIfCreated(chunkNonceKey);
            ZeroKeyIfCreated(namingKey);
            ZeroKeyIfCreated(manifestEncryptionKey);
            throw;
        }
    }

    /// <summary>
    /// Wipes an optional sub-key that was allocated before a later derivation failed.
    /// </summary>
    /// <param name="key">The key to wipe, or <see langword="null"/> when it was never created.</param>
    private static void ZeroKeyIfCreated(byte[]? key)
    {
        if (key is not null)
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Expands a 256-bit sub-key from the master key with HKDF-Expand over SHA-256.
    /// </summary>
    /// <remarks>
    /// Only the expand step is applied because the master key is already a full-length output of a
    /// password-based KDF; the context label is what keeps each sub-key bound to one purpose and
    /// independent of the others.
    /// </remarks>
    /// <param name="masterKey">The master key derived from the password.</param>
    /// <param name="context">The label identifying the sub-key's purpose.</param>
    /// <returns>The derived sub-key.</returns>
    private static byte[] DeriveSubKey(byte[] masterKey, ReadOnlySpan<byte> context)
    {
        var subKey = new byte[KeySizeBytes];
        HKDF.Expand(HashAlgorithmName.SHA256, masterKey, subKey, context);
        return subKey;
    }

    /// <summary>
    /// Generates the random master salt that seeds key derivation for a new backup.
    /// </summary>
    /// <returns>A fresh salt of <see cref="EncryptionConstants.SaltSize"/> bytes.</returns>
    private static byte[] GenerateSalt()
    {
        var salt = new byte[EncryptionConstants.SaltSize];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }

    /// <summary>
    /// Computes a chunk's on-disk name as the lowercase hex of <c>HMAC-SHA256(namingKey, chunkHash)</c>.
    /// </summary>
    /// <remarks>
    /// Keying the name keeps the content hashes off disk, so someone reading the chunks directory
    /// cannot test whether a known file is part of the backup. The intermediate HMAC is zeroed.
    /// </remarks>
    /// <param name="namingKey">The chunk-naming sub-key.</param>
    /// <param name="chunkHash">The SHA-256 content hash of the chunk.</param>
    /// <returns>The chunk file name without its extension.</returns>
    private static string ComputeChunkFileName(byte[] namingKey, byte[] chunkHash)
    {
        var hmac = HMACSHA256.HashData(namingKey, chunkHash);
        try
        {
            return Convert.ToHexStringLower(hmac);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hmac);
        }
    }

    /// <summary>
    /// Computes a chunk's full on-disk file name, extension included.
    /// </summary>
    /// <param name="namingKey">The chunk-naming sub-key.</param>
    /// <param name="chunkHash">The SHA-256 content hash of the chunk.</param>
    /// <returns>The chunk file name with its extension.</returns>
    private static string ComputeChunkFileNameWithExtension(byte[] namingKey, byte[] chunkHash)
    {
        return ComputeChunkFileName(namingKey, chunkHash) + BackupConstants.AppFileExtension;
    }

    /// <summary>
    /// Resolves the full path of a chunk inside a backup's chunks directory.
    /// </summary>
    /// <param name="chunksDir">The directory encrypted chunk files live in.</param>
    /// <param name="namingKey">The chunk-naming sub-key.</param>
    /// <param name="chunkHash">The SHA-256 content hash of the chunk.</param>
    /// <returns>The absolute path of the chunk file.</returns>
    private string ComputeChunkFilePath(string chunksDir, byte[] namingKey, byte[] chunkHash)
    {
        return fileOperationsService.CombinePath(
            chunksDir,
            ComputeChunkFileNameWithExtension(namingKey, chunkHash)
        );
    }

    /// <summary>
    /// Decodes a Base64 value read from the manifest and enforces its exact decoded length.
    /// </summary>
    /// <remarks>
    /// Length is checked before the bytes reach any cryptographic primitive, and a buffer of the
    /// wrong size is zeroed before the failure is raised.
    /// </remarks>
    /// <param name="value">The Base64 text to decode.</param>
    /// <param name="expectedLength">The exact number of bytes the value must decode to.</param>
    /// <param name="errorMessage">The message carried by the exception when validation fails.</param>
    /// <returns>The decoded bytes.</returns>
    /// <exception cref="CryptographicException">The value is not valid Base64 or decodes to the wrong length.</exception>
    private static byte[] DecodeBase64FixedLength(
        string value,
        int expectedLength,
        string errorMessage
    )
    {
        byte[] decoded;

        try
        {
            decoded = Convert.FromBase64String(value);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException(errorMessage, ex);
        }

        if (decoded.Length != expectedLength)
        {
            CryptographicOperations.ZeroMemory(decoded);
            throw new CryptographicException(errorMessage);
        }

        return decoded;
    }

    /// <summary>
    /// Owns the master key and the purpose-bound sub-keys for the duration of a single operation, so
    /// that disposing the set wipes all of them together.
    /// </summary>
    /// <param name="masterKey">The key derived from the password and the master salt.</param>
    /// <param name="chunkEncryptionKey">The sub-key chunk contents are encrypted with.</param>
    /// <param name="chunkNonceKey">The sub-key per-chunk nonces are derived from.</param>
    /// <param name="namingKey">The sub-key chunk file names are derived from.</param>
    /// <param name="manifestEncryptionKey">The sub-key the manifest is encrypted with.</param>
    private sealed class DerivedKeySet(
        byte[] masterKey,
        byte[] chunkEncryptionKey,
        byte[] chunkNonceKey,
        byte[] namingKey,
        byte[] manifestEncryptionKey
        ) : IDisposable
    {
        /// <summary>
        /// Gets the password-derived key every sub-key is expanded from.
        /// </summary>
        public byte[] MasterKey { get; } = masterKey;

        /// <summary>
        /// Gets the sub-key used to encrypt and decrypt chunk contents.
        /// </summary>
        public byte[] ChunkEncryptionKey { get; } = chunkEncryptionKey;

        /// <summary>
        /// Gets the sub-key each chunk's deterministic nonce is derived from.
        /// </summary>
        public byte[] ChunkNonceKey { get; } = chunkNonceKey;

        /// <summary>
        /// Gets the sub-key each chunk's on-disk file name is derived from.
        /// </summary>
        public byte[] NamingKey { get; } = namingKey;

        /// <summary>
        /// Gets the sub-key used to encrypt and decrypt the manifest.
        /// </summary>
        public byte[] ManifestEncryptionKey { get; } = manifestEncryptionKey;

        /// <summary>
        /// Wipes the master key and every sub-key from memory.
        /// </summary>
        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(MasterKey);
            CryptographicOperations.ZeroMemory(ChunkEncryptionKey);
            CryptographicOperations.ZeroMemory(ChunkNonceKey);
            CryptographicOperations.ZeroMemory(NamingKey);
            CryptographicOperations.ZeroMemory(ManifestEncryptionKey);
        }
    }

    /// <summary>
    /// Carries the three chunk sub-keys and the two strategies one chunking pass runs under, so a
    /// single value reaches every chunk of every file instead of five parallel arguments.
    /// </summary>
    /// <remarks>
    /// The set holds the very arrays <see cref="DerivedKeySet"/> owns rather than copies of them, so
    /// it neither extends the lifetime of any key nor changes when one is wiped: the operation's
    /// <see cref="DerivedKeySet"/> is still the only owner and still zeroes all of them on dispose.
    /// </remarks>
    /// <param name="chunkEncryptionKey">The sub-key chunk contents are encrypted with.</param>
    /// <param name="chunkNonceKey">The sub-key per-chunk nonces are derived from.</param>
    /// <param name="namingKey">The sub-key chunk file names are derived from.</param>
    /// <param name="encryptionStrategy">The strategy used to encrypt chunks.</param>
    /// <param name="compressionStrategy">The strategy applied before encryption, or <see langword="null"/> to skip compression.</param>
    private sealed class ChunkCipherSet(
        byte[] chunkEncryptionKey,
        byte[] chunkNonceKey,
        byte[] namingKey,
        IEncryptionAlgorithmStrategy encryptionStrategy,
        ICompressionStrategy? compressionStrategy
        )
    {
        /// <summary>
        /// Gets the sub-key used to encrypt chunk contents.
        /// </summary>
        public byte[] ChunkEncryptionKey { get; } = chunkEncryptionKey;

        /// <summary>
        /// Gets the sub-key each chunk's deterministic nonce is derived from.
        /// </summary>
        public byte[] ChunkNonceKey { get; } = chunkNonceKey;

        /// <summary>
        /// Gets the sub-key each chunk's on-disk file name is derived from.
        /// </summary>
        public byte[] NamingKey { get; } = namingKey;

        /// <summary>
        /// Gets the strategy used to encrypt chunks.
        /// </summary>
        public IEncryptionAlgorithmStrategy EncryptionStrategy { get; } = encryptionStrategy;

        /// <summary>
        /// Gets the strategy applied before encryption, or <see langword="null"/> when compression is disabled.
        /// </summary>
        public ICompressionStrategy? CompressionStrategy { get; } = compressionStrategy;
    }
}
