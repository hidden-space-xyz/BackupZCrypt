using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Application.Algorithms;

/// <summary>
/// The algorithms a new archive can be written with, as a front end offers them to the user.
/// </summary>
/// <remarks>
/// A value is listed exactly when the engine can write an archive with it, so a front end never
/// offers a choice the engine would then refuse. Every list is in identifier order and holds each
/// value once; the encryption and key derivation lists are never empty, and the compression list
/// always starts with <see cref="CompressionMode.None"/>.
/// </remarks>
public interface IAlgorithmCatalog
{
    /// <summary>
    /// Gets the ciphers chunks and manifests can be encrypted with.
    /// </summary>
    public IReadOnlyList<EncryptionAlgorithm> EncryptionAlgorithms { get; }

    /// <summary>
    /// Gets the functions the master key can be derived with.
    /// </summary>
    public IReadOnlyList<KeyDerivationAlgorithm> KeyDerivationAlgorithms { get; }

    /// <summary>
    /// Gets the compression modes chunks can be stored with, led by
    /// <see cref="CompressionMode.None"/>, which needs no strategy.
    /// </summary>
    public IReadOnlyList<CompressionMode> CompressionModes { get; }
}
