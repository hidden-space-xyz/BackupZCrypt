using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Desktop.Services;

/// <summary>
/// Maps each algorithm enum value to its localized display text. This replaces the
/// display name/description/summary that used to live on the Infrastructure strategy implementations:
/// presentation text now belongs entirely to the Desktop layer.
/// </summary>
internal static class AlgorithmMetadataProvider
{
    /// <summary>
    /// Gets the localized display name for an encryption algorithm.
    /// </summary>
    /// <param name="id">The encryption algorithm.</param>
    /// <returns>The localized name.</returns>
    public static string GetName(EncryptionAlgorithm id)
    {
        return id switch
        {
            EncryptionAlgorithm.Aes => Strings.AesDisplayName,
            EncryptionAlgorithm.Twofish => Strings.TwofishDisplayName,
            EncryptionAlgorithm.Serpent => Strings.SerpentDisplayName,
            EncryptionAlgorithm.ChaCha20 => Strings.ChaCha20DisplayName,
            EncryptionAlgorithm.Camellia => Strings.CamelliaDisplayName,
            _ => Strings.NoneEncryptionName,
        };
    }

    /// <summary>
    /// Gets the localized short summary for an encryption algorithm.
    /// </summary>
    /// <param name="id">The encryption algorithm.</param>
    /// <returns>The localized summary.</returns>
    public static string GetSummary(EncryptionAlgorithm id)
    {
        return id switch
        {
            EncryptionAlgorithm.Aes => Strings.AesSummary,
            EncryptionAlgorithm.Twofish => Strings.TwofishSummary,
            EncryptionAlgorithm.Serpent => Strings.SerpentSummary,
            EncryptionAlgorithm.ChaCha20 => Strings.ChaCha20Summary,
            EncryptionAlgorithm.Camellia => Strings.CamelliaSummary,
            _ => Strings.NoneEncryptionDescription,
        };
    }

    /// <summary>
    /// Gets the localized full description for an encryption algorithm.
    /// </summary>
    /// <param name="id">The encryption algorithm.</param>
    /// <returns>The localized description.</returns>
    public static string GetDescription(EncryptionAlgorithm id)
    {
        return id switch
        {
            EncryptionAlgorithm.Aes => Strings.AesDescription,
            EncryptionAlgorithm.Twofish => Strings.TwofishDescription,
            EncryptionAlgorithm.Serpent => Strings.SerpentDescription,
            EncryptionAlgorithm.ChaCha20 => Strings.ChaCha20Description,
            EncryptionAlgorithm.Camellia => Strings.CamelliaDescription,
            _ => Strings.NoneEncryptionDescription,
        };
    }

    /// <summary>
    /// Gets the localized display name for a key-derivation algorithm.
    /// </summary>
    /// <param name="id">The key-derivation algorithm.</param>
    /// <returns>The localized name.</returns>
    public static string GetName(KeyDerivationAlgorithm id)
    {
        return id switch
        {
            KeyDerivationAlgorithm.Argon2id => Strings.Argon2idDisplayName,
            KeyDerivationAlgorithm.PBKDF2 => Strings.Pbkdf2DisplayName,
            KeyDerivationAlgorithm.Scrypt => Strings.ScryptDisplayName,
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Gets the localized short summary for a key-derivation algorithm.
    /// </summary>
    /// <param name="id">The key-derivation algorithm.</param>
    /// <returns>The localized summary.</returns>
    public static string GetSummary(KeyDerivationAlgorithm id)
    {
        return id switch
        {
            KeyDerivationAlgorithm.Argon2id => Strings.Argon2idSummary,
            KeyDerivationAlgorithm.PBKDF2 => Strings.Pbkdf2Summary,
            KeyDerivationAlgorithm.Scrypt => Strings.ScryptSummary,
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Gets the localized full description for a key-derivation algorithm.
    /// </summary>
    /// <param name="id">The key-derivation algorithm.</param>
    /// <returns>The localized description.</returns>
    public static string GetDescription(KeyDerivationAlgorithm id)
    {
        return id switch
        {
            KeyDerivationAlgorithm.Argon2id => Strings.Argon2idDescription,
            KeyDerivationAlgorithm.PBKDF2 => Strings.Pbkdf2Description,
            KeyDerivationAlgorithm.Scrypt => Strings.ScryptDescription,
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Gets the localized display name for a compression mode.
    /// </summary>
    /// <param name="id">The compression mode.</param>
    /// <returns>The localized name.</returns>
    public static string GetName(CompressionMode id)
    {
        return id switch
        {
            CompressionMode.ZstdFast => Strings.ZstdFastDisplayName,
            CompressionMode.Zstd => Strings.ZstdDisplayName,
            CompressionMode.ZstdBest => Strings.ZstdBestDisplayName,
            _ => Strings.NoneCompressionName,
        };
    }

    /// <summary>
    /// Gets the localized short summary for a compression mode.
    /// </summary>
    /// <param name="id">The compression mode.</param>
    /// <returns>The localized summary.</returns>
    public static string GetSummary(CompressionMode id)
    {
        return id switch
        {
            CompressionMode.ZstdFast => Strings.ZstdFastSummary,
            CompressionMode.Zstd => Strings.ZstdSummary,
            CompressionMode.ZstdBest => Strings.ZstdBestSummary,
            _ => Strings.NoneCompressionDescription,
        };
    }

    /// <summary>
    /// Gets the localized full description for a compression mode.
    /// </summary>
    /// <param name="id">The compression mode.</param>
    /// <returns>The localized description.</returns>
    public static string GetDescription(CompressionMode id)
    {
        return id switch
        {
            CompressionMode.ZstdFast => Strings.ZstdFastDescription,
            CompressionMode.Zstd => Strings.ZstdDescription,
            CompressionMode.ZstdBest => Strings.ZstdBestDescription,
            _ => Strings.NoneCompressionDescription,
        };
    }
}
