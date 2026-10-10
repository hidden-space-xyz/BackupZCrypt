using System.Text;

namespace BackupZCrypt.Application.ValueObjects.Backup;

/// <summary>
/// Describes a create, update, or restore: the two folders it works between, the password that
/// opens or protects the archive, and whether the user already agreed to proceed past warnings.
/// </summary>
/// <remarks>
/// Each operation has its own request type, so what only one of them needs is carried only by its
/// type: <see cref="CreateBackupRequest"/> alone chooses algorithms and confirms the password,
/// because every other operation reads its algorithms from the archive itself. A verify reads one
/// folder and is never validated against a destination, so <see cref="VerifyBackupRequest"/> stands
/// on its own.
/// </remarks>
/// <param name="SourcePath">The folder the operation reads: the source tree, or the archive for a restore.</param>
/// <param name="DestinationPath">The folder the operation writes: the archive, or the restore target.</param>
/// <param name="Password">The password used to derive keys and protect the data.</param>
/// <param name="ProceedOnWarnings">Whether to continue when validation produces non-fatal warnings.</param>
public abstract record class BackupRequest(
    string SourcePath,
    string DestinationPath,
    string Password,
    bool ProceedOnWarnings
)
{
    /// <summary>
    /// Writes the record's members for <see cref="object.ToString"/>, substituting a placeholder for
    /// <see cref="Password"/>.
    /// </summary>
    /// <remarks>
    /// A record prints every member by default, so the compiler-generated implementation would put
    /// the plaintext password into any log line, exception message, or debugger watch that formats a
    /// request. There is no password recovery here, but there is also no reason for the secret to
    /// leave memory in a string; redacting it costs nothing and closes the whole class of accident.
    /// </remarks>
    /// <param name="builder">The builder receiving the formatted members.</param>
    /// <returns><see langword="true"/>, since at least one member was written.</returns>
    protected virtual bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder
            .Append("SourcePath = ").Append(this.SourcePath)
            .Append(", DestinationPath = ").Append(this.DestinationPath)
            .Append(", Password = ***")
            .Append(", ProceedOnWarnings = ").Append(this.ProceedOnWarnings);

        return true;
    }
}
