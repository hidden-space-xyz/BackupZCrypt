using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Desktop.Models;

/// <summary>
/// A selectable encryption algorithm choice with its localized display text.
/// </summary>
/// <param name="Id">The encryption algorithm identifier.</param>
/// <param name="Name">The localized display name.</param>
/// <param name="Description">The localized description or summary.</param>
public sealed record EncryptionOption(EncryptionAlgorithm Id, string Name, string Description);

/// <summary>
/// A selectable key-derivation algorithm choice with its localized display text.
/// </summary>
/// <param name="Id">The key-derivation algorithm identifier.</param>
/// <param name="Name">The localized display name.</param>
/// <param name="Description">The localized description or summary.</param>
public sealed record KeyDerivationOption(KeyDerivationAlgorithm Id, string Name, string Description);

/// <summary>
/// A selectable compression mode choice with its localized display text.
/// </summary>
/// <param name="Id">The compression mode identifier.</param>
/// <param name="Name">The localized display name.</param>
/// <param name="Description">The localized description or summary.</param>
public sealed record CompressionOption(CompressionMode Id, string Name, string Description);

/// <summary>
/// A selectable UI language choice.
/// </summary>
/// <param name="Code">The culture code (e.g. <c>en</c>, <c>es</c>), or <see langword="null"/> for the system default.</param>
/// <param name="Name">The display name shown to the user.</param>
public sealed record LanguageOption(string? Code, string Name);

/// <summary>
/// Display metadata for an algorithm, used on the About page.
/// </summary>
/// <param name="Name">The localized display name.</param>
/// <param name="Description">The localized description.</param>
public sealed record AlgorithmInfo(string Name, string Description);
