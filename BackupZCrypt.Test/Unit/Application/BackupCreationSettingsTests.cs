using BackupZCrypt.Application.Settings;
using BackupZCrypt.Domain.Algorithms;
using BackupZCrypt.Test.Common;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for <see cref="BackupCreationSettings.RestrictedTo"/>, which keeps stored defaults from
/// preselecting an algorithm this build cannot write.
/// </summary>
public sealed class BackupCreationSettingsTests
{
    [Fact]
    internal void RestrictedTo_EveryChoiceOffered_KeepsTheStoredChoices()
    {
        BackupCreationSettings stored = new(
            EncryptionAlgorithm.Serpent,
            KeyDerivationAlgorithm.Scrypt,
            CompressionMode.ZstdBest
        );

        Assert.Equal(stored, stored.RestrictedTo(AlgorithmCatalogs.Every()));
    }

    [Fact]
    internal void RestrictedTo_ChoicesNotOffered_FallBackToTheBuiltInDefaultsOneByOne()
    {
        BackupCreationSettings stored = new(
            EncryptionAlgorithm.Serpent,
            KeyDerivationAlgorithm.Scrypt,
            CompressionMode.ZstdBest
        );
        var catalog = AlgorithmCatalogs.Offering(
            [EncryptionAlgorithm.Aes, EncryptionAlgorithm.Serpent],
            [KeyDerivationAlgorithm.Argon2id],
            [CompressionMode.None, CompressionMode.Zstd]
        );

        var restricted = stored.RestrictedTo(catalog);

        Assert.Equal(
            new BackupCreationSettings(
                EncryptionAlgorithm.Serpent,
                BackupCreationSettings.DefaultValue.KeyDerivationAlgorithm,
                BackupCreationSettings.DefaultValue.CompressionMode
            ),
            restricted
        );
    }

    [Fact]
    internal void RestrictedTo_UndefinedChoices_FallBackToTheBuiltInDefaults()
    {
        BackupCreationSettings stored = new((EncryptionAlgorithm)77, (KeyDerivationAlgorithm)77, (CompressionMode)77);

        Assert.Equal(BackupCreationSettings.DefaultValue, stored.RestrictedTo(AlgorithmCatalogs.Every()));
    }

    [Fact]
    internal void RestrictedTo_DefaultsNotOfferedEither_FallBackToTheFirstOfferedChoice()
    {
        var catalog = AlgorithmCatalogs.Offering(
            [EncryptionAlgorithm.ChaCha20, EncryptionAlgorithm.Camellia],
            [KeyDerivationAlgorithm.Scrypt],
            [CompressionMode.Zstd]
        );

        var restricted = BackupCreationSettings.DefaultValue.RestrictedTo(catalog);

        Assert.Equal(
            new BackupCreationSettings(EncryptionAlgorithm.ChaCha20, KeyDerivationAlgorithm.Scrypt, CompressionMode.Zstd),
            restricted
        );
    }

    [Fact]
    internal void RestrictedTo_WithoutACatalog_IsRejected()
    {
        _ = Assert.Throws<ArgumentNullException>(() => BackupCreationSettings.DefaultValue.RestrictedTo(null!));
    }
}
