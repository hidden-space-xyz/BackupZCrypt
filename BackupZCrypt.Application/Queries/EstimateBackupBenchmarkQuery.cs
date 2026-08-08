using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Benchmark;
using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Application.Queries;

/// <summary>
/// Requests an estimate of how long creating a backup would take for a given amount of data and a
/// set of cryptographic options, measured on the current machine.
/// </summary>
/// <param name="EncryptionAlgorithm">The AEAD cipher whose chunk-encryption throughput is measured.</param>
/// <param name="KeyDerivationAlgorithm">The key derivation function whose one-time cost is measured.</param>
/// <param name="Compression">The compression mode applied to chunks before encryption.</param>
/// <param name="DataBytes">The amount of source data to estimate for, in bytes; must be greater than zero.</param>
public sealed record class EstimateBackupBenchmarkQuery(
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    CompressionMode Compression,
    long DataBytes
) : IQuery<Result<BenchmarkEstimate>>;
