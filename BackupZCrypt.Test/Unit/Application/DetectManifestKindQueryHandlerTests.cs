using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Manifest;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the detect-manifest-kind query handler: path normalization, delegation to the
/// manifest service, and the absorption of probe failures into the missing kind.
/// </summary>
public sealed class DetectManifestKindQueryHandlerTests
{
    /// <summary>
    /// An absolute backup path, so normalization leaves it unchanged.
    /// </summary>
    private static readonly string BackupPath = Path.GetFullPath(
        Path.Join(Path.GetTempPath(), "bzc-detect-backup")
    );

    /// <summary>
    /// The substituted manifest service the handler delegates to.
    /// </summary>
    private readonly IManifestService manifestService = Substitute.For<IManifestService>();

    /// <summary>
    /// Creates a handler over the substituted manifest service.
    /// </summary>
    /// <returns>The system under test.</returns>
    private DetectManifestKindQueryHandler CreateSut()
    {
        return new(this.manifestService);
    }

    [Theory]
    [InlineData(ManifestKind.Missing)]
    [InlineData(ManifestKind.Encrypted)]
    [InlineData(ManifestKind.Damaged)]
    internal async Task HandleAsync_ProbeSucceeds_ReturnsTheDetectedKind(ManifestKind kind)
    {
        _ = this.manifestService
            .DetectManifestKindAsync(BackupPath, Arg.Any<CancellationToken>())
            .Returns(kind);

        var result = await this.CreateSut()
            .HandleAsync(new DetectManifestKindQuery(BackupPath), TestContext.Current.CancellationToken);

        Assert.Equal(kind, result);
    }

    [Fact]
    internal async Task HandleAsync_PathPastedWithQuotes_IsProbedWithoutThem()
    {
        _ = this.manifestService
            .DetectManifestKindAsync(BackupPath, Arg.Any<CancellationToken>())
            .Returns(ManifestKind.Encrypted);

        var result = await this.CreateSut()
            .HandleAsync(new DetectManifestKindQuery($"\"{BackupPath}\""), TestContext.Current.CancellationToken);

        Assert.Equal(ManifestKind.Encrypted, result);
    }

    [Fact]
    internal async Task HandleAsync_RelativePath_IsReportedAsNotFoundWithoutProbing()
    {
        var result = await this.CreateSut()
            .HandleAsync(new DetectManifestKindQuery("some-backup"), TestContext.Current.CancellationToken);

        Assert.Equal(ManifestKind.PathNotFound, result);
        _ = await this.manifestService.DidNotReceive()
            .DetectManifestKindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    internal async Task HandleAsync_ProbeThrows_ReportsMissingInsteadOfLeakingTheException()
    {
        _ = this.manifestService
            .DetectManifestKindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new UnauthorizedAccessException("locked"));

        var result = await this.CreateSut()
            .HandleAsync(new DetectManifestKindQuery(BackupPath), TestContext.Current.CancellationToken);

        Assert.Equal(ManifestKind.Missing, result);
    }

    [Fact]
    internal async Task HandleAsync_ProbeCancelled_PropagatesCancellationInsteadOfMappingIt()
    {
        _ = this.manifestService
            .DetectManifestKindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => this.CreateSut()
                .HandleAsync(new DetectManifestKindQuery(BackupPath), TestContext.Current.CancellationToken)
        );
    }
}
