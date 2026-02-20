namespace CloudZCrypt.Infrastructure.Services.Encryption;

using System.Security.Cryptography;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Exceptions;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Services.Interfaces;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Domain.ValueObjects.Encryption;
using CloudZCrypt.Infrastructure.Constants;

internal sealed class EncryptionSessionFactory(
    IKeyDerivationServiceFactory keyDerivationServiceFactory) : IEncryptionSessionFactory
{
    public async Task<EncryptionSession> CreateDecryptionSessionAsync(
        Stream source,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CancellationToken cancellationToken)
    {
        byte[] salt = [];
        byte[] nonce = [];
        byte[] key = [];

        try
        {
            salt = await ReadSaltAsync(source, cancellationToken);
            nonce = await ReadNonceAsync(source, cancellationToken);
            CompressionMode compression = ReadCompressionHeader(source);
            byte[] associatedData = BuildAssociatedData(salt, nonce, compression);
            key = this.DeriveKeySafe(password, salt, keyDerivationAlgorithm);

            return new EncryptionSession(salt, nonce, key, compression, associatedData);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(nonce);
            throw;
        }
    }

    public EncryptionSession CreateEncryptionSession(
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CompressionMode compression)
    {
        byte[] salt = [];
        byte[] nonce = [];
        byte[] key = [];

        try
        {
            salt = GenerateSalt();
            nonce = GenerateNonce();
            key = this.DeriveKeySafe(password, salt, keyDerivationAlgorithm);
            byte[] associatedData = BuildAssociatedData(salt, nonce, compression);

            return new EncryptionSession(salt, nonce, key, compression, associatedData);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(nonce);
            throw;
        }
    }

    private static byte[] BuildAssociatedData(
        byte[] salt,
        byte[] nonce,
        CompressionMode compression)
    {
        byte[] header = new byte[EncryptionConstants.HeaderSize];
        Buffer.BlockCopy(salt, 0, header, 0, EncryptionConstants.SaltSize);
        Buffer.BlockCopy(
            nonce,
            0,
            header,
            EncryptionConstants.SaltSize,
            EncryptionConstants.NonceSize);
        header[EncryptionConstants.SaltSize + EncryptionConstants.NonceSize] = (byte)compression;
        return header;
    }

    private static byte[] GenerateNonce()
    {
        byte[] nonce = new byte[EncryptionConstants.NonceSize];
        RandomNumberGenerator.Fill(nonce);
        return nonce;
    }

    private static byte[] GenerateSalt()
    {
        byte[] salt = new byte[EncryptionConstants.SaltSize];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }

    private static CompressionMode ReadCompressionHeader(Stream stream)
    {
        int value = stream.ReadByte();
        if (value < 0)
        {
            throw new EndOfStreamException();
        }

        return (CompressionMode)value;
    }

    private static async Task<byte[]> ReadNonceAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        byte[] nonce = new byte[EncryptionConstants.NonceSize];
        await stream.ReadExactlyAsync(nonce, cancellationToken);
        return nonce;
    }

    private static async Task<byte[]> ReadSaltAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        byte[] salt = new byte[EncryptionConstants.SaltSize];
        await stream.ReadExactlyAsync(salt, cancellationToken);
        return salt;
    }

    private byte[] DeriveKey(
        string password,
        byte[] salt,
        int keySize,
        KeyDerivationAlgorithm algorithm)
    {
        IKeyDerivationAlgorithmStrategy keyDerivationService = keyDerivationServiceFactory.Create(
            algorithm);
        return keyDerivationService.DeriveKey(password, salt, keySize);
    }

    private byte[] DeriveKeySafe(string password, byte[] salt, KeyDerivationAlgorithm algorithm)
    {
        try
        {
            return this.DeriveKey(password, salt, EncryptionConstants.KeySize, algorithm);
        }
        catch (EncryptionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new EncryptionKeyDerivationException(ex);
        }
    }
}
