using System.Text;

using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Domain.ValueObjects.Backup;

/// <summary>
/// Describes a backup, restore, or update operation and the cryptographic options it should use.
/// </summary>
/// <param name="SourcePath">The file or directory to back up, or the backup to restore from.</param>
/// <param name="DestinationPath">The location where the backup is written or restored to.</param>
/// <param name="Password">The password used to derive keys and protect the data.</param>
/// <param name="ConfirmPassword">The password confirmation, validated against <paramref name="Password"/>.</param>
/// <param name="EncryptionAlgorithm">The AEAD cipher to encrypt chunks and the manifest with.</param>
/// <param name="KeyDerivationAlgorithm">The key derivation function used to derive the master key.</param>
/// <param name="Operation">The kind of operation to perform.</param>
/// <param name="Compression">The compression mode applied to chunks before encryption.</param>
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
