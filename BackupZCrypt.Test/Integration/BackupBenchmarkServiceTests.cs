using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Integration;

/// <summary>
/// Integration tests for the backup benchmark service's duration estimates.
/// </summary>
public sealed class BackupBenchmarkServiceTests
{
    private const long OneGigabyte = 1024L * 1024L * 1024L;

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

    [Test]
    public async Task EstimateAsync_WithoutCompression_ReturnsPositiveEstimate()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IBackupBenchmarkService>();

        var estimate = await service.EstimateAsync(NewRequest(OneGigabyte));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(estimate.EstimatedDuration, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(estimate.ThroughputBytesPerSecond, Is.GreaterThan(0));
            Assert.That(estimate.KeyDerivationDuration, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(estimate.DataBytes, Is.EqualTo(OneGigabyte));
        }
    }

    [Test]
    public async Task EstimateAsync_WithCompression_ReturnsPositiveEstimate()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IBackupBenchmarkService>();

        var estimate = await service.EstimateAsync(
            NewRequest(OneGigabyte, CompressionMode.Zstd, EncryptionAlgorithm.ChaCha20)
        );

        Assert.That(estimate.ThroughputBytesPerSecond, Is.GreaterThan(0));
    }

    [Test]
    public async Task EstimateAsync_NullRequest_Throws()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IBackupBenchmarkService>();

        _ = Assert.ThrowsAsync<ArgumentNullException>(() => service.EstimateAsync(null!));
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public async Task EstimateAsync_NonPositiveDataBytes_Throws(long dataBytes)
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IBackupBenchmarkService>();

        _ = Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.EstimateAsync(NewRequest(dataBytes))
        );
    }

    [Test]
    public async Task EstimateAsync_UnregisteredEncryptionAlgorithm_Throws()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IBackupBenchmarkService>();

        var request = NewRequest(OneGigabyte) with
        {
            EncryptionAlgorithm = (EncryptionAlgorithm)999,
        };

        _ = Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.EstimateAsync(request));
    }

    [Test]
    public async Task EstimateAsync_CancelledToken_Throws()
    {
        await using var provider = TestHost.CreateProvider();
        var service = provider.GetRequiredService<IBackupBenchmarkService>();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        _ = Assert.ThrowsAsync<OperationCanceledException>(
            () => service.EstimateAsync(NewRequest(OneGigabyte), cts.Token)
        );
    }
}
