namespace BackupZCrypt.Application.ValueObjects.Manifest;

/// <summary>
/// A chunked manifest file parsed into its parts: the unencrypted 34-byte header (algorithm, key
/// derivation, and master salt) that doubles as AEAD associated data, the nonce, and the ciphertext.
/// </summary>
/// <param name="Algorithm">The encryption algorithm that protects the manifest payload.</param>
/// <param name="KeyDerivation">The key derivation algorithm used to derive the manifest key.</param>
/// <param name="MasterSalt">The master salt bound into the preamble as associated data.</param>
/// <param name="Nonce">The nonce used to encrypt the manifest payload.</param>
/// <param name="EncryptedPayload">The AEAD-encrypted manifest document with its tag appended.</param>
public sealed record ManifestPreamble(
    Domain.Enums.EncryptionAlgorithm Algorithm,
    Domain.Enums.KeyDerivationAlgorithm KeyDerivation,
    byte[] MasterSalt,
    byte[] Nonce,
    byte[] EncryptedPayload
);
