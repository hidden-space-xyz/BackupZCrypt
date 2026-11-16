using BackupZCrypt.Desktop.Models;
using BackupZCrypt.Domain.Algorithms;

namespace BackupZCrypt.Test.Unit.Desktop.Models;

/// <summary>
/// Unit tests for the option models the pages list. Screen readers announce a list entry by its text, so each
/// model must read as its localized name rather than as its record dump.
/// </summary>
public sealed class OptionModelsTests
{
    [Fact]
    internal void ToString_ReadsAsTheDisplayedName()
    {
        Assert.Multiple(
            () => Assert.Equal("AES", new EncryptionOption(EncryptionAlgorithm.Aes, "AES", "d").ToString()),
            () => Assert.Equal("Argon2", new KeyDerivationOption(KeyDerivationAlgorithm.Argon2id, "Argon2", "d").ToString()),
            () => Assert.Equal("Zstd", new CompressionOption(CompressionMode.Zstd, "Zstd", "d").ToString()),
            () => Assert.Equal("MB", new DataSizeUnitOption("MB", 1024 * 1024).ToString()),
            () => Assert.Equal("Español", new LanguageOption("es", "Español").ToString()),
            () => Assert.Equal("Serpent", new AlgorithmInfo("Serpent", "s", "d").ToString())
        );
    }
}
