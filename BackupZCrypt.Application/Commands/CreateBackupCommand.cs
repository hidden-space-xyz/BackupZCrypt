using System.Text;

using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;

namespace BackupZCrypt.Application.Commands;

/// <summary>
/// Requests the creation of a new encrypted backup of a source directory, choosing the cipher, key
/// derivation function, and compression mode the archive is written with.
/// </summary>
/// <remarks>
/// Creation is the only operation that chooses algorithms: every other operation reads them from the
/// archive itself, so only this command carries them.
/// </remarks>
/// <param name="SourcePath">The directory to back up.</param>
/// <param name="DestinationPath">The directory the archive is written into.</param>
/// <param name="Password">The password used to derive keys and protect the data.</param>
/// <param name="ConfirmPassword">The password confirmation, validated against <paramref name="Password"/>.</param>
/// <param name="EncryptionAlgorithm">The AEAD cipher to encrypt chunks and the manifest with.</param>
/// <param name="KeyDerivationAlgorithm">The key derivation function used to derive the master key.</param>
/// <param name="Compression">The compression mode applied to chunks before encryption.</param>
/// <param name="ProceedOnWarnings">Whether to continue when validation produces non-fatal warnings.</param>
public sealed record class CreateBackupCommand(
    string SourcePath,
    string DestinationPath,
    string Password,
    string ConfirmPassword,
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    CompressionMode Compression = CompressionMode.None,
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
    /// <see cref="Password"/> and <see cref="ConfirmPassword"/> and omitting the progress sink.
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
            .Append(", DestinationPath = ").Append(this.DestinationPath)
            .Append(", Password = ***, ConfirmPassword = ***")
            .Append(", EncryptionAlgorithm = ").Append(this.EncryptionAlgorithm)
            .Append(", KeyDerivationAlgorithm = ").Append(this.KeyDerivationAlgorithm)
            .Append(", Compression = ").Append(this.Compression)
            .Append(", ProceedOnWarnings = ").Append(this.ProceedOnWarnings);

        return true;
    }
}
