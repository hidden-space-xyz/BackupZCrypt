using System.Globalization;

using BackupZCrypt.Desktop.Models;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services;
using BackupZCrypt.Desktop.ViewModels;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Unit.Desktop;

/// <summary>
/// Unit tests for the about page. Its three algorithm lists are built by the same three-line
/// expression repeated per family, so the risks worth locking down are a family described with
/// another family's metadata, a list left in registration order, and a version caption that leaks
/// the four-part assembly version.
/// </summary>
public sealed class AboutViewModelTests
{
    [Fact]
    internal void Constructor_WithTheRegisteredStrategiesInReverse_DescribesEachFamilyInIdOrder()
    {
        using var provider = TestHost.CreateProvider();
        var encryption = provider.GetServices<IEncryptionAlgorithmStrategy>().ToArray();
        var keyDerivation = provider.GetServices<IKeyDerivationAlgorithmStrategy>().ToArray();
        var compression = provider.GetServices<ICompressionStrategy>().ToArray();

        AboutViewModel sut = new(
            encryption.OrderByDescending(static strategy => strategy.Id),
            keyDerivation.OrderByDescending(static strategy => strategy.Id),
            compression.OrderByDescending(static strategy => strategy.Id)
        );

        var expectedEncryption = Describe(
            encryption.Select(static strategy => strategy.Id),
            static id => AlgorithmMetadataProvider.GetName(id),
            static id => AlgorithmMetadataProvider.GetSummary(id),
            static id => AlgorithmMetadataProvider.GetDescription(id)
        );

        var expectedKeyDerivation = Describe(
            keyDerivation.Select(static strategy => strategy.Id),
            static id => AlgorithmMetadataProvider.GetName(id),
            static id => AlgorithmMetadataProvider.GetSummary(id),
            static id => AlgorithmMetadataProvider.GetDescription(id)
        );

        var expectedCompression = Describe(
            compression.Select(static strategy => strategy.Id),
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
    internal void VersionText_ReportsTheAssemblyVersionAsThreeComponentsInTheLocalizedCaption()
    {
        AboutViewModel sut = new([], [], []);

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
    /// Builds the display metadata the page is expected to show for a family of algorithms, sorted
    /// by identifier the way the page sorts it.
    /// </summary>
    /// <typeparam name="TId">The algorithm enum describing the family.</typeparam>
    /// <param name="ids">The identifiers of the registered strategies, in any order.</param>
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
            .. ids.Order()
                .Select(id => new AlgorithmInfo(name(id), summary(id), description(id))),
        ];
    }
}
