using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Application.ValueObjects.Benchmark;

/// <summary>
/// Describes a backup-time benchmark: the cryptographic options to exercise and the amount of
/// source data whose processing time should be estimated.
/// </summary>
/// <param name="EncryptionAlgorithm">The AEAD cipher whose chunk-encryption throughput is measured.</param>
/// <param name="KeyDerivationAlgorithm">The key derivation function whose one-time cost is measured.</param>
/// <param name="Compression">The compression mode applied to chunks before encryption.</param>
/// <param name="DataBytes">The amount of source data to estimate for, in bytes; must be greater than zero.</param>
public sealed record class BenchmarkRequest(
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    CompressionMode Compression,
    long DataBytes
);
