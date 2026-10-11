using System.Globalization;

using BackupZCrypt.Application.Algorithms;
using BackupZCrypt.Desktop.Models;
using BackupZCrypt.Desktop.Presentation;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.ViewModels;
using BackupZCrypt.Domain.Algorithms;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Unit.Desktop.ViewModels;

/// <summary>
/// Unit tests for the about page. Its three algorithm lists are built by the same three-line
/// expression repeated per family, so the risks worth locking down are a family described with
/// another family's metadata, a list reordered away from the catalog's order, and a version
/// caption that leaks the four-part assembly version.
/// </summary>
public sealed class AboutViewModelTests
{
    [Fact]
    internal void Constructor_WithTheRegisteredCatalog_DescribesEachFamilyInCatalogOrder()
    {
        using var provider = TestHost.CreateProvider();
        var catalog = provider.GetRequiredService<IAlgorithmCatalog>();

        AboutViewModel sut = new(catalog);

        var expectedEncryption = Describe(
            catalog.EncryptionAlgorithms,
            static id => AlgorithmMetadataProvider.GetName(id),
            static id => AlgorithmMetadataProvider.GetSummary(id),
            static id => AlgorithmMetadataProvider.GetDescription(id)
        );

        var expectedKeyDerivation = Describe(
            catalog.KeyDerivationAlgorithms,
            static id => AlgorithmMetadataProvider.GetName(id),
            static id => AlgorithmMetadataProvider.GetSummary(id),
            static id => AlgorithmMetadataProvider.GetDescription(id)
        );

        var expectedCompression = Describe(
            catalog.CompressionModes,
            static id => AlgorithmMetadataProvider.GetName(id),
            static id => AlgorithmMetadataProvider.GetSummary(id),
            static id => AlgorithmMetadataProvider.GetDescription(id)
        );

        Assert.Multiple(
            () => Assert.NotEmpty(expectedEncryption),
            () => Assert.NotEmpty(expectedKeyDerivation),
            () => Assert.NotEmpty(expectedCompression),
            () => Assert.Equal(expectedEncryption, sut.EncryptionAlgorithms),
            () => Assert.Equal(expectedKeyDerivation, sut.KeyDerivationAlgorithms),
            () => Assert.Equal(expectedCompression, sut.CompressionAlgorithms)
        );
    }

    [Fact]
    internal void Constructor_KeepsTheOrderTheCatalogGivesEachFamily()
    {
        var catalog = AlgorithmCatalogs.Offering(
            [EncryptionAlgorithm.Serpent, EncryptionAlgorithm.Aes],
            [KeyDerivationAlgorithm.Scrypt, KeyDerivationAlgorithm.Argon2id],
            [CompressionMode.Zstd, CompressionMode.None]
        );

        AboutViewModel sut = new(catalog);

        Assert.Multiple(
            () =>
                Assert.Equal(
                    catalog.EncryptionAlgorithms.Select(static id => AlgorithmMetadataProvider.GetName(id)),
                    sut.EncryptionAlgorithms.Select(static info => info.Name)
                ),
            () =>
                Assert.Equal(
                    catalog.KeyDerivationAlgorithms.Select(static id => AlgorithmMetadataProvider.GetName(id)),
                    sut.KeyDerivationAlgorithms.Select(static info => info.Name)
                ),
            () =>
                Assert.Equal(
                    catalog.CompressionModes.Select(static id => AlgorithmMetadataProvider.GetName(id)),
                    sut.CompressionAlgorithms.Select(static info => info.Name)
                )
        );
    }

    [Fact]
    internal void VersionText_ReportsTheAssemblyVersionAsThreeComponentsInTheLocalizedCaption()
    {
        AboutViewModel sut = new(AlgorithmCatalogs.Offering([], [], []));

        var version = typeof(AboutViewModel).Assembly.GetName().Version;
        var expectedVersion = version is null
            ? "1.0.0"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{version.Major}.{version.Minor}.{version.Build}"
            );

        Assert.Equal(
            string.Format(CultureInfo.CurrentCulture, Strings.VersionFormat, expectedVersion),
            sut.VersionText
        );
    }

    /// <summary>
    /// Builds the display metadata the page is expected to show for a family of algorithms, in the
    /// order the catalog lists them.
    /// </summary>
    /// <typeparam name="TId">The algorithm enum describing the family.</typeparam>
    /// <param name="ids">The identifiers the catalog lists, in its order.</param>
    /// <param name="name">The provider's display-name accessor for the family.</param>
    /// <param name="summary">The provider's summary accessor for the family.</param>
    /// <param name="description">The provider's description accessor for the family.</param>
    /// <returns>The expected rows, in the order the page must list them.</returns>
    private static AlgorithmInfo[] Describe<TId>(
        IEnumerable<TId> ids,
        Func<TId, string> name,
        Func<TId, string> summary,
        Func<TId, string> description
    )
        where TId : struct, Enum
    {
        return
        [
            .. ids.Select(id => new AlgorithmInfo(name(id), summary(id), description(id))),
        ];
    }
}
