using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Desktop.Models;

/// <summary>
/// A selectable key-derivation algorithm choice with its localized display text.
/// </summary>
/// <param name="Id">The key-derivation algorithm identifier.</param>
/// <param name="Name">The localized display name.</param>
/// <param name="Description">The localized description or summary.</param>
internal sealed record KeyDerivationOption(
    KeyDerivationAlgorithm Id,
    string Name,
    string Description
);
