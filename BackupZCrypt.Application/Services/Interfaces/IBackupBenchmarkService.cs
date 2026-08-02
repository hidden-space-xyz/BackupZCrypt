using BackupZCrypt.Application.ValueObjects.Benchmark;

namespace BackupZCrypt.Application.Services.Interfaces;

/// <summary>
/// Estimates how long creating a backup would take for a given amount of data and a set of
/// cryptographic options, by measuring the real chunking, hashing, compression, encryption, and
/// key-derivation strategies on synthetic data on the current machine.
/// </summary>
public interface IBackupBenchmarkService
{
    /// <summary>
    /// Runs a short, in-memory benchmark of the selected algorithms and extrapolates the measured
    /// throughput to the requested amount of data. The estimate models CPU-bound processing time
    /// (chunking, hashing, optional compression, and encryption) plus the one-time key derivation
    /// cost; it does not include disk read/write time, which depends on the destination device.
    /// </summary>
    /// <param name="request">The cryptographic options to exercise and the amount of data to estimate for.</param>
    /// <param name="cancellationToken">A token to cancel the benchmark.</param>
    /// <returns>An estimate of the total processing time and the measurements it was derived from.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="BenchmarkRequest.DataBytes"/> is not greater than zero, or the requested encryption
    /// algorithm is not registered.
    /// </exception>
    public Task<BenchmarkEstimate> EstimateAsync(
        BenchmarkRequest request,
        CancellationToken cancellationToken = default
    );
}
