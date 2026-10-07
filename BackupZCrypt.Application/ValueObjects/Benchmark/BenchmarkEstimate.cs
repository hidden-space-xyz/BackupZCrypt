namespace BackupZCrypt.Application.ValueObjects.Benchmark;

/// <summary>
/// The immutable result of a backup-time benchmark: the estimated total duration for the requested
/// amount of data and the measurements it was derived from.
/// </summary>
/// <param name="EstimatedDuration">
/// The estimated total time to process the requested amount of data, including the one-time key derivation cost.
/// </param>
/// <param name="ThroughputBytesPerSecond">
/// The measured effective processing throughput (chunking, hashing, optional compression, and encryption) across as
/// many concurrent file pipelines as a real backup runs on this machine, in source bytes per second.
/// </param>
/// <param name="KeyDerivationDuration">
/// The measured one-time cost of deriving the master key with the selected key derivation function.
/// </param>
/// <param name="DataBytes">The amount of source data the estimate was computed for, in bytes.</param>
/// <param name="LargeFileEstimatedDuration">
/// The estimated total time when the data is a single large file, which a backup reads with one
/// pipeline while still encrypting its chunks in parallel, or <see langword="null"/> when not measured.
/// </param>
/// <param name="LargeFileThroughputBytesPerSecond">
/// The measured throughput of a single large file, in source bytes per second, or zero when not measured.
/// </param>
public sealed record class BenchmarkEstimate(
    TimeSpan EstimatedDuration,
    double ThroughputBytesPerSecond,
    TimeSpan KeyDerivationDuration,
    long DataBytes,
    TimeSpan? LargeFileEstimatedDuration = null,
    double LargeFileThroughputBytesPerSecond = 0
);
