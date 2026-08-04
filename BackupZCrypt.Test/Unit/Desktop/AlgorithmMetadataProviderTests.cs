using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services;
using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Test.Unit.Desktop;

/// <summary>
/// Unit tests for <see cref="AlgorithmMetadataProvider"/>. Every accessor ends in a catch-all arm, so an
/// algorithm added to an enum and registered in the container still compiles and ships with a blank — or,
/// for compression, a plausible but wrong "no compression" — label. These tests are driven from
/// <c>Enum.GetValues</c> so that the missing metadata fails the build instead of reaching the picker the
/// user chooses their encryption, key derivation, and compression from.
/// </summary>
public sealed class AlgorithmMetadataProviderTests
{
    [Test]
    public void Metadata_EveryEncryptionAlgorithm_IsNamedSummarizedAndDescribedUniquely()
    {
        AssertEveryValueIsDescribedUniquely(
            Enum.GetValues<EncryptionAlgorithm>(),
            static id => AlgorithmMetadataProvider.GetName(id),
            static id => AlgorithmMetadataProvider.GetSummary(id),
            static id => AlgorithmMetadataProvider.GetDescription(id)
        );
    }

    [Test]
    public void Metadata_EveryKeyDerivationAlgorithm_IsNamedSummarizedAndDescribedUniquely()
    {
        AssertEveryValueIsDescribedUniquely(
            Enum.GetValues<KeyDerivationAlgorithm>(),
            static id => AlgorithmMetadataProvider.GetName(id),
            static id => AlgorithmMetadataProvider.GetSummary(id),
            static id => AlgorithmMetadataProvider.GetDescription(id)
        );
    }

    [Test]
    public void Metadata_EveryCompressionModeExceptNone_DiffersFromTheNoneFallbackText()
    {
        var noneName = Strings.NoneCompressionName;
        var noneDescription = Strings.NoneCompressionDescription;

        var unlabeled = Enum.GetValues<CompressionMode>()
            .Where(mode =>
                mode is not CompressionMode.None
                && (
                    string.Equals(AlgorithmMetadataProvider.GetName(mode), noneName, StringComparison.Ordinal)
                    || string.Equals(AlgorithmMetadataProvider.GetSummary(mode), noneDescription, StringComparison.Ordinal)
                    || string.Equals(AlgorithmMetadataProvider.GetDescription(mode), noneDescription, StringComparison.Ordinal)
                )
            )
            .Select(static mode => mode.ToString())
            .ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                unlabeled,
                Is.Empty,
                "CompressionMode members that fall through to the \"no compression\" text, which would describe a "
                    + "compressing mode as storing chunks uncompressed: "
                    + string.Join(", ", unlabeled)
            );
            Assert.That(
                AlgorithmMetadataProvider.GetName(CompressionMode.None),
                Is.EqualTo(noneName),
                "CompressionMode.None must keep the fallback name; it is the one value the catch-all arm is meant for."
            );
        }
    }

    /// <summary>
    /// Asserts that every value of an algorithm enum carries a non-blank name, summary, and description, and
    /// that each of those three sets is duplicate-free, so a copy-pasted arm is caught as well as a missing one.
    /// </summary>
    /// <typeparam name="TId">The algorithm enum being described.</typeparam>
    /// <param name="ids">Every value the enum defines.</param>
    /// <param name="name">The provider's display-name accessor.</param>
    /// <param name="summary">The provider's summary accessor.</param>
    /// <param name="description">The provider's description accessor.</param>
    private static void AssertEveryValueIsDescribedUniquely<TId>(
        TId[] ids,
        Func<TId, string> name,
        Func<TId, string> summary,
        Func<TId, string> description
    )
        where TId : struct, Enum
    {
        var names = ids.Select(name).ToList();
        var summaries = ids.Select(summary).ToList();
        var descriptions = ids.Select(description).ToList();

        var blank = ids.Where(
                (_, index) =>
                    string.IsNullOrWhiteSpace(names[index])
                    || string.IsNullOrWhiteSpace(summaries[index])
                    || string.IsNullOrWhiteSpace(descriptions[index])
            )
            .Select(static id => id.ToString())
            .ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                blank,
                Is.Empty,
                $"{typeof(TId).Name} members with blank display metadata, which would render as an empty row: "
                    + string.Join(", ", blank)
            );
            Assert.That(names, Is.Unique, $"{typeof(TId).Name} display names must be distinct: {string.Join(" | ", names)}");
            Assert.That(summaries, Is.Unique, $"{typeof(TId).Name} summaries must be distinct: {string.Join(" | ", summaries)}");
            Assert.That(
                descriptions,
                Is.Unique,
                $"{typeof(TId).Name} descriptions must be distinct so no algorithm is described as another one."
            );
        }
    }
}
