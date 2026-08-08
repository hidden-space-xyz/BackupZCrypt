using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Manifest;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the detect-manifest-kind query handler: delegation to the manifest service and the
/// absorption of probe failures into the missing kind.
/// </summary>
public sealed class DetectManifestKindQueryHandlerTests
{
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

    [TestCase(ManifestKind.Missing)]
    [TestCase(ManifestKind.Encrypted)]
    public async Task HandleAsync_ProbeSucceeds_ReturnsTheDetectedKind(ManifestKind kind)
    {
        _ = this.manifestService
            .DetectManifestKindAsync("some-backup", Arg.Any<CancellationToken>())
            .Returns(kind);

        var result = await this.CreateSut()
            .HandleAsync(new DetectManifestKindQuery("some-backup"), CancellationToken.None);

        Assert.That(result, Is.EqualTo(kind));
    }

    [Test]
    public async Task HandleAsync_ProbeThrows_ReportsMissingInsteadOfLeakingTheException()
    {
        _ = this.manifestService
            .DetectManifestKindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new UnauthorizedAccessException("locked"));

        var result = await this.CreateSut()
            .HandleAsync(new DetectManifestKindQuery("locked-backup"), CancellationToken.None);

        Assert.That(result, Is.EqualTo(ManifestKind.Missing));
    }

    [Test]
    public void HandleAsync_ProbeCancelled_PropagatesCancellationInsteadOfMappingIt()
    {
        _ = this.manifestService
            .DetectManifestKindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        _ = Assert.ThrowsAsync<OperationCanceledException>(
            () => this.CreateSut()
                .HandleAsync(new DetectManifestKindQuery("some-backup"), CancellationToken.None)
        );
    }
}
