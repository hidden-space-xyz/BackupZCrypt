using System.Text;

using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Application.Commands;

/// <summary>
/// Requests the restoration of an existing encrypted backup into a destination directory. The
/// archive's own cipher, key derivation function, and compression mode are read from it, so the
/// command carries no algorithm choices.
/// </summary>
/// <param name="BackupPath">The archive to read from.</param>
/// <param name="DestinationPath">The directory the files are rebuilt into.</param>
/// <param name="Password">The password the archive was created with.</param>
/// <param name="ProceedOnWarnings">Whether to continue when validation produces non-fatal warnings.</param>
public sealed record class RestoreBackupCommand(
    string BackupPath,
    string DestinationPath,
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
            .Append("BackupPath = ").Append(this.BackupPath)
            .Append(", DestinationPath = ").Append(this.DestinationPath)
            .Append(", Password = ***")
            .Append(", ProceedOnWarnings = ").Append(this.ProceedOnWarnings);

        return true;
    }
}
