using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Worker;

internal sealed class WorkerConfiguration
{
    public string BackupSourcePath { get; set; } = "/data/backup-source";

    public string BackupDestinationPath { get; set; } = "/data/backup-destination";

    public string RestoreSourcePath { get; set; } = "/data/restore-source";

    public string RestoreDestinationPath { get; set; } = "/data/restore-destination";

    public string Password { get; set; } = string.Empty;

    public EncryptionAlgorithm EncryptionAlgorithm { get; set; } = EncryptionAlgorithm.Aes;

    public KeyDerivationAlgorithm KeyDerivationAlgorithm { get; set; } = KeyDerivationAlgorithm.Argon2id;

    public CompressionMode Compression { get; set; } = CompressionMode.None;

    public bool DeleteSourceFiles { get; set; }
}
