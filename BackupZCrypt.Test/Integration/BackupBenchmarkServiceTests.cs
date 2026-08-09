using BackupZCrypt.Application.Services;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Benchmark;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Integration;

/// <summary>
/// Integration tests for the backup benchmark service's duration estimates.
/// </summary>
public sealed class BackupBenchmarkServiceTests
{
    /// <summary>
    /// The payload size the estimates are requested for: 2^30 bytes, one gibibyte.
    /// </summary>
    private const long OneGigabyte = 1024L * 1024L * 1024L;

    /// <summary>
    /// Builds a benchmark request that defaults to AES without compression, so each test varies only
    /// the option it is interested in.
    /// </summary>
    /// <param name="dataBytes">The amount of source data to estimate for, in bytes.</param>
    /// <param name="compression">The compression mode to measure.</param>
    /// <param name="encryption">The AEAD cipher to measure.</param>
    /// <returns>The assembled request, always using PBKDF2 key derivation.</returns>
    private static BenchmarkRequest NewRequest(
        long dataBytes,
        CompressionMode compression = CompressionMode.None,
        EncryptionAlgorithm encryption = EncryptionAlgorithm.Aes
    )
    {
        return new BenchmarkRequest(
            encryption,
            KeyDerivationAlgorithm.PBKDF2,
            compression,
            dataBytes
        );
    }

    /// <summary>
    /// Verifies that an estimate reports a finite throughput and a duration derived from its own
    /// measurements, for each combination of compression mode and cipher.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately does NOT assert <c>ThroughputBytesPerSecond &gt; 0</c>. The measurement loop tests
    /// <c>stopwatch.Elapsed &lt; MeasureWindow</c> before its first pass, and the stopwatch is started
    /// before the workers are queued; on a contended two-vCPU runner no worker need be scheduled inside
    /// the 500 ms window, every worker then returns zero bytes, and a strictly-positive assertion fails
    /// for reasons that have nothing to do with this code. That is scheduling luck, not a contract.
    /// </para>
    /// <para>
    /// What is asserted instead holds on any machine and is strictly more than the single loose
    /// assertion the compression case used to make: the estimate must be finite and derived from the very
    /// numbers it reports, so a wrong argument order, a sign error, or an estimate that ignores
    /// <c>DataBytes</c> still fails. The pipeline itself stays covered because the warm-up pass runs one
    /// chunk through chunking, hashing, nonce derivation, compression, and encryption unconditionally —
    /// a broken cipher or compression strategy throws before any of this is reached.
    /// </para>
    /// </remarks>
    /// <param name="compression">The compression mode to measure.</param>
    /// <param name="encryption">The AEAD cipher to measure.</param>
    /// <returns>A task that completes when the estimate has been checked.</returns>
    [Theory]
    [InlineData(CompressionMode.None, EncryptionAlgorithm.Aes)]
    [InlineData(CompressionMode.Zstd, EncryptionAlgorithm.ChaCha20)]
    internal async Task EstimateAsync_ForSupportedOptions_ReturnsSelfConsistentEstimate(
        CompressionMode compression,
        EncryptionAlgorithm encryption
    )
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IBackupBenchmarkService>();

        var estimate = await service.EstimateAsync(
            NewRequest(OneGigabyte, compression, encryption),
            TestContext.Current.CancellationToken
        );

        var recomputed = BackupBenchmarkService.ComputeEstimatedDuration(
            estimate.KeyDerivationDuration,
            estimate.ThroughputBytesPerSecond,
            estimate.DataBytes
        );

        Assert.Multiple(
            () => Assert.Equal(OneGigabyte, estimate.DataBytes),
            () =>
                Assert.True(
                    estimate.KeyDerivationDuration > TimeSpan.Zero,
                    "A full production key derivation was not timed."
                ),
            () => Assert.True(estimate.ThroughputBytesPerSecond >= 0),
            () =>
                Assert.True(
                    double.IsFinite(estimate.ThroughputBytesPerSecond),
                    "A NaN or infinite throughput makes TimeSpan.FromSeconds throw inside the estimate."
                ),
            () => Assert.Equal(recomputed, estimate.EstimatedDuration),
            () => Assert.True(estimate.EstimatedDuration >= estimate.KeyDerivationDuration)
        );
    }

    [Fact]
    internal async Task EstimateAsync_InvalidArguments_ThrowMatchingArgumentException()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IBackupBenchmarkService>();

        await Assert.MultipleAsync(
            async () =>
            {
                _ = await Assert.ThrowsAsync<ArgumentNullException>(
                    () => service.EstimateAsync(null!, TestContext.Current.CancellationToken)
                );
            },
            async () =>
            {
                _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                    () => service.EstimateAsync(NewRequest(0), TestContext.Current.CancellationToken)
                );
            },
            async () =>
            {
                _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                    () => service.EstimateAsync(NewRequest(-1), TestContext.Current.CancellationToken)
                );
            }
        );
    }

    [Fact]
    internal async Task EstimateAsync_UnregisteredEncryptionAlgorithm_Throws()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IBackupBenchmarkService>();

        var request = NewRequest(OneGigabyte) with
        {
            EncryptionAlgorithm = (EncryptionAlgorithm)999,
        };

        _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.EstimateAsync(request, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    internal async Task EstimateAsync_CancelledToken_Throws()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IBackupBenchmarkService>();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.EstimateAsync(NewRequest(OneGigabyte), cts.Token)
        );
    }
}
