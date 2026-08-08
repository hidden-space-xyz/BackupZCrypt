using System.Text;

using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Application.Queries;

/// <summary>
/// Requests a read-only verification of an existing encrypted backup, reconstructing every file in
/// memory and checking it against the manifest without writing anything to disk.
/// </summary>
/// <remarks>
/// Verification skips request validation entirely and never produces advisory warnings, so unlike
/// the backup commands this query carries no proceed-on-warnings flag.
/// </remarks>
/// <param name="BackupPath">The archive to verify.</param>
/// <param name="Password">The password the archive was created with.</param>
public sealed record class VerifyBackupQuery(
    string BackupPath,
    string Password
) : IQuery<Result<BackupOutcome>>
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
    /// the query. Redacting it costs nothing and closes the whole class of accident.
    /// </remarks>
    /// <param name="builder">The builder receiving the formatted members.</param>
    /// <returns><see langword="true"/>, since at least one member was written.</returns>
    private bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder
            .Append("BackupPath = ").Append(this.BackupPath)
            .Append(", Password = ***");

        return true;
    }
}
