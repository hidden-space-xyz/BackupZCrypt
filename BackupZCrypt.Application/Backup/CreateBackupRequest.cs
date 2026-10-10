using System.Text;

using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Application.Backup;

/// <summary>
/// Describes the creation of a new archive of a source folder, and the cipher, key derivation
/// function, and compression mode it is written with.
/// </summary>
/// <param name="SourcePath">The folder to back up.</param>
/// <param name="DestinationPath">The folder the archive is written into.</param>
/// <param name="Password">The password used to derive keys and protect the data.</param>
/// <param name="ConfirmPassword">The password confirmation, validated against <paramref name="Password"/>.</param>
/// <param name="EncryptionAlgorithm">The AEAD cipher to encrypt chunks and the manifest with.</param>
/// <param name="KeyDerivationAlgorithm">The key derivation function used to derive the master key.</param>
/// <param name="Compression">The compression mode applied to chunks before encryption.</param>
/// <param name="ProceedOnWarnings">Whether to continue when validation produces non-fatal warnings.</param>
public sealed record class CreateBackupRequest(
    string SourcePath,
    string DestinationPath,
    string Password,
    string ConfirmPassword,
    EncryptionAlgorithm EncryptionAlgorithm,
    KeyDerivationAlgorithm KeyDerivationAlgorithm,
    CompressionMode Compression = CompressionMode.None,
    bool ProceedOnWarnings = false
) : BackupRequest(SourcePath, DestinationPath, Password, ProceedOnWarnings)
{
    /// <summary>
    /// Writes the record's members for <see cref="object.ToString"/>, substituting a placeholder for
    /// both passwords as <see cref="BackupRequest"/> does for the one it carries.
    /// </summary>
    /// <param name="builder">The builder receiving the formatted members.</param>
    /// <returns><see langword="true"/>, since at least one member was written.</returns>
    protected override bool PrintMembers(StringBuilder builder)
    {
        _ = base.PrintMembers(builder);
        _ = builder
            .Append(", ConfirmPassword = ***")
            .Append(", EncryptionAlgorithm = ").Append(this.EncryptionAlgorithm)
            .Append(", KeyDerivationAlgorithm = ").Append(this.KeyDerivationAlgorithm)
            .Append(", Compression = ").Append(this.Compression);

        return true;
    }
}
