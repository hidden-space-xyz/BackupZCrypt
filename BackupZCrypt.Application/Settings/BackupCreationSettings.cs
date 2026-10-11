using BackupZCrypt.Application.Algorithms;
using BackupZCrypt.Domain.Algorithms;

namespace BackupZCrypt.Application.Settings;

/// <summary>
/// Persisted defaults preselected in the UI when creating a new backup.
/// </summary>
/// <param name="EncryptionAlgorithm">The encryption algorithm preselected for new backups.</param>
/// <param name="KeyDerivationAlgorithm">The key derivation algorithm preselected for new backups.</param>
/// <param name="CompressionMode">The compression mode preselected for new backups.</param>
public sealed record class BackupCreationSettings(
    EncryptionAlgorithm EncryptionAlgorithm = EncryptionAlgorithm.Aes,
    KeyDerivationAlgorithm KeyDerivationAlgorithm = KeyDerivationAlgorithm.Argon2id,
    CompressionMode CompressionMode = CompressionMode.None
) : ISettings<BackupCreationSettings>
{
    /// <summary>
    /// Gets the default settings used when none have been persisted.
    /// </summary>
    public static BackupCreationSettings DefaultValue { get; } = new();

    /// <summary>
    /// Gets the file name under which these settings are stored.
    /// </summary>
    public static string FileName => "backup-creation-settings.json";

    /// <summary>
    /// Returns these defaults with every choice the catalog does not offer replaced, so a value read
    /// from a hand-edited or newer settings file never preselects an algorithm this build cannot write.
    /// </summary>
    /// <remarks>
    /// Each choice falls back on its own: first to the built-in default, then to the first algorithm
    /// the catalog offers when even the built-in default is not offered.
    /// </remarks>
    /// <param name="catalog">The algorithms this build can write an archive with.</param>
    /// <returns>Defaults whose every choice the catalog offers.</returns>
    public BackupCreationSettings RestrictedTo(IAlgorithmCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        return new(
            Offered(this.EncryptionAlgorithm, DefaultValue.EncryptionAlgorithm, catalog.EncryptionAlgorithms),
            Offered(this.KeyDerivationAlgorithm, DefaultValue.KeyDerivationAlgorithm, catalog.KeyDerivationAlgorithms),
            Offered(this.CompressionMode, DefaultValue.CompressionMode, catalog.CompressionModes)
        );
    }

    /// <summary>
    /// Picks the preferred value when it is offered, otherwise the fallback, otherwise the first value
    /// offered.
    /// </summary>
    /// <typeparam name="TId">The algorithm enum.</typeparam>
    /// <param name="preferred">The stored choice.</param>
    /// <param name="fallback">The built-in default.</param>
    /// <param name="offered">The values the catalog offers; never empty.</param>
    /// <returns>An offered value.</returns>
    private static TId Offered<TId>(TId preferred, TId fallback, IReadOnlyList<TId> offered)
        where TId : struct, Enum
    {
        return offered.Contains(preferred) ? preferred
            : offered.Contains(fallback) ? fallback
            : offered[0];
    }
}
