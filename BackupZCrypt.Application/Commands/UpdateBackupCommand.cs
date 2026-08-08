using System.Text;

using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Application.Commands;

/// <summary>
/// Requests an update of an existing encrypted backup from the current contents of a source
/// directory. The archive's own cipher, key derivation function, and compression mode are read from
/// it, so the command carries no algorithm choices.
/// </summary>
/// <param name="SourcePath">The directory whose current contents feed the update.</param>
/// <param name="BackupPath">The existing archive to update.</param>
/// <param name="Password">The password the archive was created with.</param>
/// <param name="ProceedOnWarnings">Whether to continue when validation produces non-fatal warnings.</param>
public sealed record class UpdateBackupCommand(
    string SourcePath,
    string BackupPath,
    string Password,
    bool ProceedOnWarnings = false
) : ICommand<Result<BackupOutcome>>
{
    /// <summary>
    /// Gets the sink that receives incremental status updates, or <see langword="null"/> to discard
    /// them. Non-positional because it is transport for the operation, not data describing it.
    /// </summary>
    public IProgress<BackupStatus>? Progress { get; init; }

    /// <summary>
    /// Writes the record's members for <see cref="object.ToString"/>, substituting a placeholder for
    /// <see cref="Password"/> and omitting the progress sink.
    /// </summary>
    /// <remarks>
    /// A record prints every member by default, so the compiler-generated implementation would put
    /// the plaintext password into any log line, exception message, or debugger watch that formats
    /// the command. Redacting it costs nothing and closes the whole class of accident.
    /// </remarks>
    /// <param name="builder">The builder receiving the formatted members.</param>
    /// <returns><see langword="true"/>, since at least one member was written.</returns>
    private bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder
            .Append("SourcePath = ").Append(this.SourcePath)
            .Append(", BackupPath = ").Append(this.BackupPath)
            .Append(", Password = ***")
            .Append(", ProceedOnWarnings = ").Append(this.ProceedOnWarnings);

        return true;
    }
}
