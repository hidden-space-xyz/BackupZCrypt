using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Desktop.Models;

/// <summary>
/// A selectable encryption algorithm choice with its localized display text.
/// </summary>
/// <param name="Id">The encryption algorithm identifier.</param>
/// <param name="Name">The localized display name.</param>
/// <param name="Description">The localized description or summary.</param>
internal sealed record EncryptionOption(EncryptionAlgorithm Id, string Name, string Description);
