using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Benchmark;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Localization;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the estimate-backup-benchmark query handler: the mapping of the query onto a
/// benchmark request, and the mapping of service failures onto the result contract.
/// </summary>
public sealed class EstimateBackupBenchmarkQueryHandlerTests
{
    /// <summary>
    /// The substituted benchmark service the handler delegates to.
    /// </summary>
    private readonly IBackupBenchmarkService benchmarkService =
        Substitute.For<IBackupBenchmarkService>();

    /// <summary>
    /// Creates a handler over the substituted benchmark service.
    /// </summary>
    /// <returns>The system under test.</returns>
    private EstimateBackupBenchmarkQueryHandler CreateSut()
    {
        return new(this.benchmarkService);
    }

    [Fact]
    internal async Task HandleAsync_Query_MapsEveryFieldOntoABenchmarkRequestAndReturnsTheEstimate()
    {
        BenchmarkEstimate estimate = new(TimeSpan.FromSeconds(12), 1_000_000, TimeSpan.FromSeconds(1), 1_000_000_000);

        BenchmarkRequest? captured = null;
        _ = this.benchmarkService
            .EstimateAsync(Arg.Do<BenchmarkRequest>(request => captured = request), Arg.Any<CancellationToken>())
            .Returns(estimate);

        var query = new EstimateBackupBenchmarkQuery(
            EncryptionAlgorithm.Serpent,
            KeyDerivationAlgorithm.Scrypt,
            CompressionMode.ZstdFast,
            1_000_000_000
        );

        var result = await this.CreateSut().HandleAsync(query, CancellationToken.None);

        Assert.Multiple(
            () => Assert.True(result.IsSuccess),
            () => Assert.Same(estimate, result.Value),
            () => Assert.Equal(EncryptionAlgorithm.Serpent, captured!.EncryptionAlgorithm),
            () => Assert.Equal(KeyDerivationAlgorithm.Scrypt, captured!.KeyDerivationAlgorithm),
            () => Assert.Equal(CompressionMode.ZstdFast, captured!.Compression),
            () => Assert.Equal(1_000_000_000L, captured!.DataBytes)
        );
    }

    [Fact]
    internal async Task HandleAsync_ServiceThrows_ReportsUnexpectedErrorCarryingOnlyTheMessage()
    {
        _ = this.benchmarkService
            .EstimateAsync(Arg.Any<BenchmarkRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("benchmark boom"));

        var query = new EstimateBackupBenchmarkQuery(
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            CompressionMode.None,
            1
        );

        var result = await this.CreateSut().HandleAsync(query, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () => Assert.Single(result.Errors),
            () => Assert.Equal(MessageCode.UnexpectedErrorFormat, result.Errors[0].Code)
        );
    }

    [Fact]
    internal async Task HandleAsync_BenchmarkCancelled_PropagatesCancellationInsteadOfMappingIt()
    {
        _ = this.benchmarkService
            .EstimateAsync(Arg.Any<BenchmarkRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var query = new EstimateBackupBenchmarkQuery(
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            CompressionMode.None,
            1
        );

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => this.CreateSut().HandleAsync(query, CancellationToken.None)
        );
    }
}
