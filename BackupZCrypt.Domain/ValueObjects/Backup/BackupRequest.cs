namespace BackupZCrypt.Domain.ValueObjects.Backup;

using BackupZCrypt.Domain.Enums;

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