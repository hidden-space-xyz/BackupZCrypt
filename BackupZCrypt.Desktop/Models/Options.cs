using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Desktop.Models;

/// <summary>
/// A selectable encryption algorithm choice with its localized display text.
/// </summary>
/// <param name="Id">The encryption algorithm identifier.</param>
/// <param name="Name">The localized display name.</param>
/// <param name="Description">The localized description or summary.</param>
internal sealed record EncryptionOption(EncryptionAlgorithm Id, string Name, string Description);

/// <summary>
/// A selectable key-derivation algorithm choice with its localized display text.
/// </summary>
/// <param name="Id">The key-derivation algorithm identifier.</param>
/// <param name="Name">The localized display name.</param>
/// <param name="Description">The localized description or summary.</param>
internal sealed record KeyDerivationOption(KeyDerivationAlgorithm Id, string Name, string Description);

/// <summary>
/// A selectable compression mode choice with its localized display text.
/// </summary>
/// <param name="Id">The compression mode identifier.</param>
/// <param name="Name">The localized display name.</param>
/// <param name="Description">The localized description or summary.</param>
internal sealed record CompressionOption(CompressionMode Id, string Name, string Description);

/// <summary>
/// A selectable UI language choice.
/// </summary>
/// <param name="Code">The culture code (for example, <c>en</c>, <c>es</c>), or <see langword="null"/> for the system default.</param>
/// <param name="Name">The display name shown to the user.</param>
internal sealed record LanguageOption(string? Code, string Name);

/// <summary>
/// A selectable data-size unit used by the benchmark to convert a user-entered amount into bytes.
/// </summary>
/// <param name="Name">The unit symbol shown to the user (for example, <c>MB</c>, <c>GB</c>, <c>TB</c>).</param>
/// <param name="BytesPerUnit">The number of bytes in one unit, using binary (1024-based) multiples.</param>
internal sealed record DataSizeUnitOption(string Name, long BytesPerUnit);

/// <summary>
/// Display metadata for an algorithm, used on the help page.
/// </summary>
/// <param name="Name">The localized display name.</param>
/// <param name="Summary">The localized one-line takeaway.</param>
/// <param name="Description">The localized description.</param>
internal sealed record AlgorithmInfo(string Name, string Summary, string Description);
