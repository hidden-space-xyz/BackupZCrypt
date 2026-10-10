using System.Text;

namespace BackupZCrypt.Application.Backup;

/// <summary>
/// Describes a read-only integrity check of an existing archive. It writes nothing, so it names no
/// destination, and it reads its algorithms from the archive, so it chooses none.
/// </summary>
/// <param name="BackupPath">The folder holding the archive to verify.</param>
/// <param name="Password">The password the archive was created with.</param>
public sealed record class VerifyBackupRequest(string BackupPath, string Password)
{
    /// <summary>
    /// Writes the record's members for <see cref="object.ToString"/>, substituting a placeholder for
    /// <see cref="Password"/> so the secret never reaches a log line or debugger watch.
    /// </summary>
    /// <param name="builder">The builder receiving the formatted members.</param>
    /// <returns><see langword="true"/>, since at least one member was written.</returns>
    private bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.Append("BackupPath = ").Append(this.BackupPath).Append(", Password = ***");

        return true;
    }
}
