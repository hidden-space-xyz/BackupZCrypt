using System.Security.Cryptography;
using System.Text;

using BackupZCrypt.Domain.Algorithms;

namespace BackupZCrypt.Domain.Archive;

/// <summary>
/// The key schedule of an archive: the master key derived from the password and the master salt,
/// and the purpose-bound sub-keys expanded from it for chunk encryption, nonce derivation, chunk
/// naming, and the manifest. Disposing the set wipes every key it holds.
/// </summary>
/// <remarks>
/// Every archive already on disk was keyed this way, so the sub-key labels, their order, and the
/// password normalization are part of the format and are pinned by the committed format fixtures.
/// </remarks>
public sealed class ArchiveKeys : IDisposable
{
    /// <summary>
    /// The length in bytes of the master key and of every sub-key.
    /// </summary>
    private const int KeySizeBytes = EncryptionConstants.KeySize / 8;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArchiveKeys"/> class, taking ownership of every key.
    /// </summary>
    /// <param name="masterKey">The key derived from the password and the master salt.</param>
    /// <param name="chunkEncryptionKey">The sub-key chunk contents are encrypted with.</param>
    /// <param name="chunkNonceKey">The sub-key per-chunk nonces are derived from.</param>
    /// <param name="namingKey">The sub-key chunk file names are derived from.</param>
    /// <param name="manifestEncryptionKey">The sub-key the manifest is encrypted with.</param>
    private ArchiveKeys(
        byte[] masterKey,
        byte[] chunkEncryptionKey,
        byte[] chunkNonceKey,
        byte[] namingKey,
        byte[] manifestEncryptionKey
    )
    {
        this.MasterKey = masterKey;
        this.ChunkEncryptionKey = chunkEncryptionKey;
        this.ChunkNonceKey = chunkNonceKey;
        this.NamingKey = namingKey;
        this.ManifestEncryptionKey = manifestEncryptionKey;
    }

    /// <summary>
    /// Gets the password-derived key every sub-key is expanded from.
    /// </summary>
    public byte[] MasterKey { get; }

    /// <summary>
    /// Gets the sub-key used to encrypt and decrypt chunk contents.
    /// </summary>
    public byte[] ChunkEncryptionKey { get; }

    /// <summary>
    /// Gets the sub-key each chunk's deterministic nonce is derived from.
    /// </summary>
    public byte[] ChunkNonceKey { get; }

    /// <summary>
    /// Gets the sub-key each chunk's on-disk file name is derived from.
    /// </summary>
    public byte[] NamingKey { get; }

    /// <summary>
    /// Gets the sub-key used to encrypt and decrypt the manifest.
    /// </summary>
    public byte[] ManifestEncryptionKey { get; }

    /// <summary>
    /// Derives the key set of an archive from the password the user typed.
    /// </summary>
    /// <remarks>
    /// The password is normalized to Unicode normalization form C first, so the same characters open
    /// the archive whichever composed or decomposed form the keyboard, input method, or operating
    /// system produced.
    /// </remarks>
    /// <param name="password">The password as the user typed it.</param>
    /// <param name="masterSalt">The master salt generated for or read from the archive.</param>
    /// <param name="keyDerivation">The strategy implementing the archive's key derivation function.</param>
    /// <returns>The derived key set, owned by the caller.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static ArchiveKeys Derive(
        string password,
        byte[] masterSalt,
        IKeyDerivationAlgorithmStrategy keyDerivation
    )
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(masterSalt);
        ArgumentNullException.ThrowIfNull(keyDerivation);

        return FromMasterKey(
            keyDerivation.DeriveKey(NormalizePassword(password), masterSalt, EncryptionConstants.KeySize)
        );
    }

    /// <summary>
    /// Expands the purpose-bound sub-keys from a master key with HKDF-Expand over SHA-256.
    /// </summary>
    /// <remarks>
    /// Only the expand step is applied because the master key is already a full-length output of a
    /// password-based KDF; the context label is what keeps each sub-key bound to one purpose and
    /// independent of the others. The master key is zeroed if expansion fails, so no key material
    /// survives an error.
    /// </remarks>
    /// <param name="masterKey">The master key, whose ownership passes to the returned set.</param>
    /// <returns>The key set holding the master key and its sub-keys.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="masterKey"/> is <see langword="null"/>.</exception>
    public static ArchiveKeys FromMasterKey(byte[] masterKey)
    {
        ArgumentNullException.ThrowIfNull(masterKey);

        byte[]? chunkEncryptionKey = null;
        byte[]? chunkNonceKey = null;
        byte[]? namingKey = null;
        byte[]? manifestEncryptionKey = null;

        try
        {
            chunkEncryptionKey = ExpandSubKey(masterKey, "chunk-encryption"u8);
            chunkNonceKey = ExpandSubKey(masterKey, "chunk-nonce"u8);
            namingKey = ExpandSubKey(masterKey, "chunk-naming"u8);
            manifestEncryptionKey = ExpandSubKey(masterKey, "manifest-encryption"u8);

            return new ArchiveKeys(
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
            ZeroIfCreated(chunkEncryptionKey);
            ZeroIfCreated(chunkNonceKey);
            ZeroIfCreated(namingKey);
            ZeroIfCreated(manifestEncryptionKey);
            throw;
        }
    }

    /// <summary>
    /// Generates the random master salt that seeds key derivation for a new archive.
    /// </summary>
    /// <returns>A fresh salt of <see cref="EncryptionConstants.SaltSize"/> bytes.</returns>
    public static byte[] GenerateMasterSalt()
    {
        var salt = new byte[EncryptionConstants.SaltSize];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }

    /// <summary>
    /// Wipes the master key and every sub-key from memory.
    /// </summary>
    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(this.MasterKey);
        CryptographicOperations.ZeroMemory(this.ChunkEncryptionKey);
        CryptographicOperations.ZeroMemory(this.ChunkNonceKey);
        CryptographicOperations.ZeroMemory(this.NamingKey);
        CryptographicOperations.ZeroMemory(this.ManifestEncryptionKey);
    }

    /// <summary>
    /// Returns the form of a password that keys are derived from: Unicode normalization form C, so the
    /// same characters typed in either composed or decomposed form derive the same keys.
    /// </summary>
    /// <param name="password">The password as the user typed it.</param>
    /// <returns>The composed password, or the password as typed when it is not valid Unicode.</returns>
    private static string NormalizePassword(string password)
    {
        try
        {
            return password.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return password;
        }
    }

    /// <summary>
    /// Expands one 256-bit sub-key from the master key.
    /// </summary>
    /// <param name="masterKey">The master key derived from the password.</param>
    /// <param name="context">The label identifying the sub-key's purpose.</param>
    /// <returns>The derived sub-key.</returns>
    private static byte[] ExpandSubKey(byte[] masterKey, ReadOnlySpan<byte> context)
    {
        var subKey = new byte[KeySizeBytes];
        HKDF.Expand(HashAlgorithmName.SHA256, masterKey, subKey, context);
        return subKey;
    }

    /// <summary>
    /// Wipes an optional sub-key that was allocated before a later expansion failed.
    /// </summary>
    /// <param name="key">The key to wipe, or <see langword="null"/> when it was never created.</param>
    private static void ZeroIfCreated(byte[]? key)
    {
        if (key is not null)
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
