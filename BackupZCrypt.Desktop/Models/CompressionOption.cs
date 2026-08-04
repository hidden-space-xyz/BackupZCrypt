using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Desktop.Models;

/// <summary>
/// A selectable compression mode choice with its localized display text.
/// </summary>
/// <param name="Id">The compression mode identifier.</param>
/// <param name="Name">The localized display name.</param>
/// <param name="Description">The localized description or summary.</param>
internal sealed record class CompressionOption(CompressionMode Id, string Name, string Description);
