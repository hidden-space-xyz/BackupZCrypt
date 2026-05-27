namespace BackupZCrypt.Application.ValueObjects.Manifest;

public sealed record ManifestPreamble(
    BackupZCrypt.Domain.Enums.EncryptionAlgorithm Algorithm,
    BackupZCrypt.Domain.Enums.KeyDerivationAlgorithm KeyDerivation,
    byte[] MasterSalt,
    byte[] Nonce,
    byte[] EncryptedPayload
);
