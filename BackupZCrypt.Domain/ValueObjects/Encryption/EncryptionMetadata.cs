namespace BackupZCrypt.Domain.ValueObjects.Encryption;

using BackupZCrypt.Domain.Enums;

public sealed record EncryptionMetadata(byte[] Salt, byte[] Nonce, CompressionMode Compression);
