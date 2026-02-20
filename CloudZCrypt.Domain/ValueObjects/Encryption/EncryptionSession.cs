namespace CloudZCrypt.Domain.ValueObjects.Encryption;

using System.Security.Cryptography;
using CloudZCrypt.Domain.Enums;

public sealed class EncryptionSession(
    byte[] salt,
    byte[] nonce,
    byte[] key,
    CompressionMode compression,
    byte[] associatedData) : IDisposable
{
    public byte[] Salt { get; } = salt;

    public byte[] Nonce { get; } = nonce;

    public byte[] Key { get; } = key;

    public CompressionMode Compression { get; } = compression;

    public byte[] AssociatedData { get; } = associatedData;

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(this.Key);
        CryptographicOperations.ZeroMemory(this.Salt);
        CryptographicOperations.ZeroMemory(this.Nonce);
    }
}
