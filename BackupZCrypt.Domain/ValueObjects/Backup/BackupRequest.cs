using System.Text;

using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Domain.ValueObjects.Backup;

/// <summary>
/// Describes a backup, restore, update, or verify operation and the cryptographic options it should use.
/// </summary>
/// <remarks>
/// <see cref="EncryptionAlgorithm"/>, <see cref="KeyDerivationAlgorithm"/>, and
/// <see cref="Compression"/> are inputs **only** for <see cref="BackupOperation.Create"/>. Every
/// other operation reads the algorithms it must use from the archive itself — the manifest preamble
/// for the cipher and KDF, the manifest header for compression — because using anything else would
/// derive the wrong key and fail to open the archive. Build those requests with
/// <see cref="ForRestore"/>, <see cref="ForUpdate"/>, or <see cref="ForVerify"/> rather than
/// choosing values the operation will discard.
/// </remarks>
/// <param name="SourcePath">The file or directory to back up, or the backup to restore from.</param>
/// <param name="DestinationPath">The location where the backup is written or restored to.</param>
/// <param name="Password">The password used to derive keys and protect the data.</param>
/// <param name="ConfirmPassword">The password confirmation, validated against <paramref name="Password"/>.</param>
/// <param name="EncryptionAlgorithm">The AEAD cipher to encrypt chunks and the manifest with; used only when creating.</param>
/// <param name="KeyDerivationAlgorithm">The key derivation function used to derive the master key; used only when creating.</param>
/// <param name="Operation">The kind of operation to perform.</param>
/// <param name="Compression">The compression mode applied to chunks before encryption; used only when creating.</param>
/// <param name="ProceedOnWarnings">Whether to continue when validation produces non-fatal warnings.</param>
public sealed record BackupRequest(
    string SourcePath,
    string DestinationPath,
    string Password,
    string ConfirmPassword,
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    BackupOperation Operation,
    CompressionMode Compression = CompressionMode.None,
    bool ProceedOnWarnings = false
)
{
    /// <summary>
    /// The algorithm placeholders used by the operations that read their algorithms from the archive.
    /// </summary>
    /// <remarks>
    /// These values are never applied: the engine overwrites them from the manifest preamble before
    /// any key is derived. They exist because the record requires the fields, and naming them here
    /// once is what stops three call sites from each picking an arbitrary cipher and looking as
    /// though it mattered.
    /// </remarks>
    private const EncryptionAlgorithm UnusedAlgorithm = Enums.EncryptionAlgorithm.Aes;

    /// <summary>
    /// The key derivation placeholder used by the operations that read theirs from the archive.
    /// </summary>
    private const KeyDerivationAlgorithm UnusedKeyDerivation =
        Enums.KeyDerivationAlgorithm.Argon2id;

    /// <summary>
    /// Builds a request that restores an existing archive into a destination directory.
    /// </summary>
    /// <param name="backupPath">The archive to read from.</param>
    /// <param name="destinationPath">The directory the files are rebuilt into.</param>
    /// <param name="password">The password the archive was created with.</param>
    /// <param name="proceedOnWarnings">Whether to continue past non-fatal validation warnings.</param>
    /// <returns>The configured restore request.</returns>
    public static BackupRequest ForRestore(
        string backupPath,
        string destinationPath,
        string password,
        bool proceedOnWarnings
    ) =>
        new(
            backupPath,
            destinationPath,
            password,
            password,
            UnusedAlgorithm,
            UnusedKeyDerivation,
            BackupOperation.Restore,
            CompressionMode.None,
            proceedOnWarnings
        );

    /// <summary>
    /// Builds a request that updates an existing archive from a source directory.
    /// </summary>
    /// <param name="sourcePath">The directory whose current contents feed the update.</param>
    /// <param name="backupPath">The existing archive to update.</param>
    /// <param name="password">The password the archive was created with.</param>
    /// <param name="proceedOnWarnings">Whether to continue past non-fatal validation warnings.</param>
    /// <returns>The configured update request.</returns>
    public static BackupRequest ForUpdate(
        string sourcePath,
        string backupPath,
        string password,
        bool proceedOnWarnings
    ) =>
        new(
            sourcePath,
            backupPath,
            password,
            password,
            UnusedAlgorithm,
            UnusedKeyDerivation,
            BackupOperation.Update,
            CompressionMode.None,
            proceedOnWarnings
        );

    /// <summary>
    /// Builds a request that verifies an archive without writing anything.
    /// </summary>
    /// <param name="backupPath">The archive to verify.</param>
    /// <param name="password">The password the archive was created with.</param>
    /// <param name="proceedOnWarnings">Whether to continue past non-fatal validation warnings.</param>
    /// <returns>The configured verify request, whose destination is empty because verification is read-only.</returns>
    public static BackupRequest ForVerify(
        string backupPath,
        string password,
        bool proceedOnWarnings
    ) =>
        new(
            backupPath,
            string.Empty,
            password,
            password,
            UnusedAlgorithm,
            UnusedKeyDerivation,
            BackupOperation.Verify,
            CompressionMode.None,
            proceedOnWarnings
        );

    /// <summary>
    /// Writes the record's members for <see cref="object.ToString"/>, substituting a placeholder for
    /// <see cref="Password"/> and <see cref="ConfirmPassword"/>.
    /// </summary>
    /// <remarks>
    /// A record prints every member by default, so the compiler-generated implementation would put
    /// the plaintext password into any log line, exception message, or debugger watch that formats a
    /// request. There is no password recovery here, but there is also no reason for the secret to
    /// leave memory in a string; redacting it costs nothing and closes the whole class of accident.
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
            .Append(", Operation = ").Append(this.Operation)
            .Append(", Compression = ").Append(this.Compression)
            .Append(", ProceedOnWarnings = ").Append(this.ProceedOnWarnings);

        return true;
    }
}
