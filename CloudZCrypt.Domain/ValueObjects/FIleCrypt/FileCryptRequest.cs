namespace CloudZCrypt.Domain.ValueObjects.FileCrypt;

using CloudZCrypt.Domain.Enums;

public sealed record FileCryptRequest(
    string SourcePath,
    string DestinationPath,
    string Password,
    string ConfirmPassword,
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    EncryptOperation Operation,
    NameObfuscationMode NameObfuscation,
    CompressionMode Compression = CompressionMode.None,
    bool ProceedOnWarnings = false);
