using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Desktop.Services;

/// <summary>
/// Maps encryption, key-derivation, and compression enum values to their localized display text.
/// </summary>
/// <remarks>
/// Presentation text belongs entirely to the Desktop layer, so the strategy implementations in Infrastructure
/// carry no display name, summary, or description of their own. The members are grouped by overload rather
/// than by algorithm family so every <c>GetName</c>, <c>GetSummary</c>, and <c>GetDescription</c> sits next
/// to its siblings.
/// </remarks>
internal static class AlgorithmMetadataProvider
{
    /// <summary>
    /// Gets the localized display name for an encryption algorithm.
    /// </summary>
    /// <param name="id">The encryption algorithm.</param>
    /// <returns>The localized name, or an empty string when the value is not a known algorithm.</returns>
    public static string GetName(EncryptionAlgorithm id)
    {
        return id switch
        {
            EncryptionAlgorithm.Aes => Strings.AesDisplayName,
            EncryptionAlgorithm.Twofish => Strings.TwofishDisplayName,
            EncryptionAlgorithm.Serpent => Strings.SerpentDisplayName,
            EncryptionAlgorithm.ChaCha20 => Strings.ChaCha20DisplayName,
            EncryptionAlgorithm.Camellia => Strings.CamelliaDisplayName,
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Gets the localized display name for a key-derivation algorithm.
    /// </summary>
    /// <param name="id">The key-derivation algorithm.</param>
    /// <returns>The localized name, or an empty string when the value is not a known algorithm.</returns>
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
    /// Gets the localized display name for a compression mode.
    /// </summary>
    /// <param name="id">The compression mode.</param>
    /// <returns>The localized name for a Zstandard level, or the "no compression" name for every other value.</returns>
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
    /// Gets the localized short summary for an encryption algorithm.
    /// </summary>
    /// <param name="id">The encryption algorithm.</param>
    /// <returns>The localized summary, or an empty string when the value is not a known algorithm.</returns>
    public static string GetSummary(EncryptionAlgorithm id)
    {
        return id switch
        {
            EncryptionAlgorithm.Aes => Strings.AesSummary,
            EncryptionAlgorithm.Twofish => Strings.TwofishSummary,
            EncryptionAlgorithm.Serpent => Strings.SerpentSummary,
            EncryptionAlgorithm.ChaCha20 => Strings.ChaCha20Summary,
            EncryptionAlgorithm.Camellia => Strings.CamelliaSummary,
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Gets the localized short summary for a key-derivation algorithm.
    /// </summary>
    /// <param name="id">The key-derivation algorithm.</param>
    /// <returns>The localized summary, or an empty string when the value is not a known algorithm.</returns>
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
    /// Gets the localized short summary for a compression mode.
    /// </summary>
    /// <param name="id">The compression mode.</param>
    /// <returns>
    /// The localized summary for a Zstandard level, or the "no compression" description for every other value,
    /// because no separate "no compression" summary resource exists.
    /// </returns>
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
    /// Gets the localized full description for an encryption algorithm.
    /// </summary>
    /// <param name="id">The encryption algorithm.</param>
    /// <returns>The localized description, or an empty string when the value is not a known algorithm.</returns>
    public static string GetDescription(EncryptionAlgorithm id)
    {
        return id switch
        {
            EncryptionAlgorithm.Aes => Strings.AesDescription,
            EncryptionAlgorithm.Twofish => Strings.TwofishDescription,
            EncryptionAlgorithm.Serpent => Strings.SerpentDescription,
            EncryptionAlgorithm.ChaCha20 => Strings.ChaCha20Description,
            EncryptionAlgorithm.Camellia => Strings.CamelliaDescription,
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Gets the localized full description for a key-derivation algorithm.
    /// </summary>
    /// <param name="id">The key-derivation algorithm.</param>
    /// <returns>The localized description, or an empty string when the value is not a known algorithm.</returns>
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
    /// Gets the localized full description for a compression mode.
    /// </summary>
    /// <param name="id">The compression mode.</param>
    /// <returns>The localized description for a Zstandard level, or the "no compression" description for every other value.</returns>
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
