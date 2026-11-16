using BackupZCrypt.Application.Algorithms;
using BackupZCrypt.Application.Benchmark;
using BackupZCrypt.Domain.Algorithms;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Application.Benchmark;

/// <summary>
/// Unit tests for how <see cref="BackupBenchmarkService.EstimateAsync"/> treats a measurement that
/// fails before it starts.
/// </summary>
public sealed class BackupBenchmarkServiceEstimateTests
{
    [Fact]
    internal async Task EstimateAsync_OutOfMemory_PropagatesInsteadOfReportingAnUnexpectedError()
    {
        var encryption = Substitute.For<IStrategyRegistry<EncryptionAlgorithm, IEncryptionAlgorithmStrategy>>();
        _ = encryption.Resolve(Arg.Any<EncryptionAlgorithm>()).Returns(_ => throw new InsufficientMemoryException());
        BackupBenchmarkService service = new(
            encryption,
            Substitute.For<IStrategyRegistry<CompressionMode, ICompressionStrategy>>(),
            Substitute.For<IChunkingStrategy>(),
            Substitute.For<IStrategyRegistry<KeyDerivationAlgorithm, IKeyDerivationAlgorithmStrategy>>()
        );

        _ = await Assert.ThrowsAsync<InsufficientMemoryException>(
            () =>
                service.EstimateAsync(
                    new BenchmarkRequest(
                        EncryptionAlgorithm.Aes,
                        KeyDerivationAlgorithm.PBKDF2,
                        CompressionMode.None,
                        1024
                    ),
                    TestContext.Current.CancellationToken
                )
        );
    }
}
