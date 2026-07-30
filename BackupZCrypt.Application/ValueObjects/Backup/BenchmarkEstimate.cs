namespace BackupZCrypt.Application.ValueObjects.Backup;

/// <summary>
/// The immutable result of a backup-time benchmark: the estimated total duration for the requested
/// amount of data and the measurements it was derived from.
/// </summary>
/// <param name="EstimatedDuration">
/// The estimated total time to process the requested amount of data, including the one-time key derivation cost.
/// </param>
/// <param name="ThroughputBytesPerSecond">
/// The measured effective processing throughput (chunking, hashing, optional compression, and encryption) across all
/// logical processors, in source bytes per second.
/// </param>
/// <param name="KeyDerivationDuration">
/// The measured one-time cost of deriving the master key with the selected key derivation function.
/// </param>
/// <param name="DataBytes">The amount of source data the estimate was computed for, in bytes.</param>
public sealed record BenchmarkEstimate(
    TimeSpan EstimatedDuration,
    double ThroughputBytesPerSecond,
    TimeSpan KeyDerivationDuration,
    long DataBytes
);
