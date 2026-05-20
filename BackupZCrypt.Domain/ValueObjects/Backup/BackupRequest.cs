using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Domain.ValueObjects.Backup;

public sealed record BackupRequest(
    string SourcePath,
    string DestinationPath,
    string Password,
    string ConfirmPassword,
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    BackupOperation Operation,
    CompressionMode Compression = CompressionMode.None,
    bool ProceedOnWarnings = false);