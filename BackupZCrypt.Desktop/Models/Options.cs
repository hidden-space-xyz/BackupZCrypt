using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Desktop.Models;

public sealed record EncryptionOption(EncryptionAlgorithm Id, string Name, string Description);

public sealed record KeyDerivationOption(KeyDerivationAlgorithm Id, string Name, string Description);

public sealed record CompressionOption(CompressionMode Id, string Name, string Description);

public sealed record LanguageOption(string? Code, string Name);

public sealed record AlgorithmInfo(string Name, string Description);
