namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// The unencrypted header of a chunked manifest file, carrying the parameters needed to
/// derive keys and decrypt the manifest payload.
/// </summary>
/// <param name="Algorithm">The encryption algorithm that protects the manifest payload.</param>
/// <param name="KeyDerivation">The key derivation algorithm used to derive the manifest key.</param>
/// <param name="MasterSalt">The master salt bound into the preamble as associated data.</param>
/// <param name="Nonce">The nonce used to encrypt the manifest payload.</param>
/// <param name="EncryptedPayload">The AEAD-encrypted manifest document with its tag appended.</param>
public sealed record ManifestPreamble(
    BackupZCrypt.Domain.Enums.EncryptionAlgorithm Algorithm,
    BackupZCrypt.Domain.Enums.KeyDerivationAlgorithm KeyDerivation,
    byte[] MasterSalt,
    byte[] Nonce,
    byte[] EncryptedPayload
);
