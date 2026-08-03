namespace BackupZCrypt.Desktop.Models;

/// <summary>
/// Display metadata for an algorithm, used on the help page.
/// </summary>
/// <param name="Name">The localized display name.</param>
/// <param name="Summary">The localized one-line takeaway.</param>
/// <param name="Description">The localized description.</param>
internal sealed record AlgorithmInfo(string Name, string Summary, string Description);
