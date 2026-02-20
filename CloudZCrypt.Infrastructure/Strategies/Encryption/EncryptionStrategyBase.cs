using System.Security.Cryptography;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Exceptions;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Infrastructure.Resources;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Modes;

namespace CloudZCrypt.Infrastructure.Strategies.Encryption;

internal abstract class EncryptionStrategyBase(
    IKeyDerivationServiceFactory keyDerivationServiceFactory,
    ICompressionServiceFactory compressionServiceFactory
)
{
    protected const int KeySize = 256;
    protected const int SaltSize = 32;
    protected const int NonceSize = 12;
    protected const int CompressionHeaderSize = 1;
    protected const int MacSize = 128;
    protected const int BufferSize = 80 * 1024;
    private const int HeaderSize = SaltSize + NonceSize + CompressionHeaderSize;

    public async Task<bool> EncryptFileAsync(
        string sourceFilePath,
        string destinationFilePath,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CompressionMode compression = CompressionMode.None
    )
    {
        try
        {
            if (!File.Exists(sourceFilePath))
            {
                throw new EncryptionFileNotFoundException(sourceFilePath);
            }

            try
            {
                using FileStream testRead = File.OpenRead(sourceFilePath);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new EncryptionAccessDeniedException(sourceFilePath, ex);
            }

            EnsureDirectoryExists(destinationFilePath);
            ValidateDiskSpace(sourceFilePath, destinationFilePath);

            byte[] salt = GenerateSalt();
            byte[] nonce = GenerateNonce();
            byte[] key = [];

            try
            {
                try
                {
                    key = DeriveKey(password, salt, KeySize, keyDerivationAlgorithm);
                }
                catch (Exception ex)
                {
                    throw new EncryptionKeyDerivationException(ex);
                }

                using Stream sourceFile = File.OpenRead(sourceFilePath);
                using Stream destinationFile = File.Create(destinationFilePath);

                await WriteSaltAsync(destinationFile, salt);
                await WriteNonceAsync(destinationFile, nonce);
                WriteCompressionHeader(destinationFile, compression);

                ICompressionStrategy compressionStrategy = compressionServiceFactory.Create(
                    compression
                );
                using Stream compressedSource = await compressionStrategy.CompressAsync(sourceFile);
                await EncryptStreamAsync(compressedSource, destinationFile, key, nonce);
            }
            catch (EncryptionException)
            {
                throw;
            }
            catch (IOException ex)
            {
                TryDeleteFile(destinationFilePath);

                if (ex.Message.Contains("space", StringComparison.OrdinalIgnoreCase))
                {
                    throw new EncryptionInsufficientSpaceException(destinationFilePath);
                }
                throw new EncryptionCipherException(Messages.OperationEncryption, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new EncryptionAccessDeniedException(destinationFilePath, ex);
            }
            catch (Exception ex)
            {
                TryDeleteFile(destinationFilePath);

                throw new EncryptionCipherException(Messages.OperationEncryption, ex);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(salt);
                CryptographicOperations.ZeroMemory(nonce);
            }

            return true;
        }
        catch (EncryptionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new EncryptionCipherException(Messages.OperationEncryption, ex);
        }
    }

    public async Task<bool> DecryptFileAsync(
        string sourceFilePath,
        string destinationFilePath,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm
    )
    {
        try
        {
            if (!File.Exists(sourceFilePath))
            {
                throw new EncryptionFileNotFoundException(sourceFilePath);
            }

            try
            {
                using Stream testRead = File.OpenRead(sourceFilePath);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new EncryptionAccessDeniedException(sourceFilePath, ex);
            }

            FileInfo fileInfo = new(sourceFilePath);
            if (fileInfo.Length < HeaderSize)
            {
                throw new EncryptionCorruptedFileException(sourceFilePath);
            }

            EnsureDirectoryExists(destinationFilePath);

            byte[] salt = [];
            byte[] nonce = [];
            byte[] key = [];

            try
            {
                using Stream sourceFile = File.OpenRead(sourceFilePath);

                salt = await ReadSaltAsync(sourceFile);
                nonce = await ReadNonceAsync(sourceFile);
                CompressionMode compression = ReadCompressionHeader(sourceFile);

                try
                {
                    key = DeriveKey(password, salt, KeySize, keyDerivationAlgorithm);
                }
                catch (Exception ex)
                {
                    throw new EncryptionKeyDerivationException(ex);
                }

                using MemoryStream decryptedBuffer = new();
                await DecryptStreamAsync(sourceFile, decryptedBuffer, key, nonce);
                decryptedBuffer.Position = 0;

                ICompressionStrategy compressionStrategy = compressionServiceFactory.Create(
                    compression
                );
                using Stream decompressedStream = await compressionStrategy.DecompressAsync(
                    decryptedBuffer
                );

                using Stream destinationFile = File.Create(destinationFilePath);
                await decompressedStream.CopyToAsync(destinationFile, BufferSize);
            }
            catch (EncryptionException)
            {
                throw;
            }
            catch (InvalidCipherTextException)
            {
                TryDeleteFile(destinationFilePath);
                throw new EncryptionInvalidPasswordException();
            }
            catch (CryptographicException)
            {
                TryDeleteFile(destinationFilePath);
                throw new EncryptionInvalidPasswordException();
            }
            catch (EndOfStreamException)
            {
                TryDeleteFile(destinationFilePath);
                throw new EncryptionCorruptedFileException(sourceFilePath);
            }
            catch (IOException ex)
            {
                TryDeleteFile(destinationFilePath);

                if (ex.Message.Contains("space", StringComparison.OrdinalIgnoreCase))
                {
                    throw new EncryptionInsufficientSpaceException(destinationFilePath);
                }
                throw new EncryptionCipherException(Messages.OperationDecryption, ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new EncryptionAccessDeniedException(destinationFilePath, ex);
            }
            catch (Exception ex)
            {
                TryDeleteFile(destinationFilePath);

                throw new EncryptionCipherException(Messages.OperationDecryption, ex);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(salt);
                CryptographicOperations.ZeroMemory(nonce);
            }

            return true;
        }
        catch (EncryptionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new EncryptionCipherException(Messages.OperationDecryption, ex);
        }
    }

    public virtual async Task<bool> CreateEncryptedFileAsync(
        byte[] plaintextData,
        string destinationFilePath,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CompressionMode compression = CompressionMode.None
    )
    {
        ArgumentNullException.ThrowIfNull(plaintextData);

        EnsureDirectoryExists(destinationFilePath);

        byte[] salt = GenerateSalt();
        byte[] nonce = GenerateNonce();
        byte[] key = [];

        try
        {
            try
            {
                key = DeriveKey(password, salt, KeySize, keyDerivationAlgorithm);
            }
            catch (Exception ex)
            {
                throw new EncryptionKeyDerivationException(ex);
            }

            using Stream destinationFile = File.Create(destinationFilePath);
            await WriteSaltAsync(destinationFile, salt);
            await WriteNonceAsync(destinationFile, nonce);
            WriteCompressionHeader(destinationFile, compression);

            using Stream source = new MemoryStream(plaintextData, writable: false);
            ICompressionStrategy compressionStrategy = compressionServiceFactory.Create(
                compression
            );
            using Stream compressedSource = await compressionStrategy.CompressAsync(source);
            await EncryptStreamAsync(compressedSource, destinationFile, key, nonce);
        }
        catch (EncryptionException)
        {
            throw;
        }
        catch (IOException ex)
        {
            TryDeleteFile(destinationFilePath);

            if (ex.Message.Contains("space", StringComparison.OrdinalIgnoreCase))
            {
                throw new EncryptionInsufficientSpaceException(destinationFilePath);
            }
            throw new EncryptionCipherException(Messages.OperationEncryption, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new EncryptionAccessDeniedException(destinationFilePath, ex);
        }
        catch (Exception ex)
        {
            TryDeleteFile(destinationFilePath);

            throw new EncryptionCipherException(Messages.OperationEncryption, ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(nonce);
        }

        return true;
    }

    public virtual async Task<byte[]> ReadEncryptedFileAsync(
        string sourceFilePath,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm
    )
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new EncryptionFileNotFoundException(sourceFilePath);
        }

        byte[] salt = [];
        byte[] nonce = [];
        byte[] key = [];

        try
        {
            using Stream source = File.OpenRead(sourceFilePath);

            if (source.Length < HeaderSize)
            {
                throw new EncryptionCorruptedFileException(sourceFilePath);
            }

            salt = await ReadSaltAsync(source);
            nonce = await ReadNonceAsync(source);
            CompressionMode compression = ReadCompressionHeader(source);

            try
            {
                key = DeriveKey(password, salt, KeySize, keyDerivationAlgorithm);
            }
            catch (Exception ex)
            {
                throw new EncryptionKeyDerivationException(ex);
            }

            using MemoryStream decryptedBuffer = new();
            await DecryptStreamAsync(source, decryptedBuffer, key, nonce);
            decryptedBuffer.Position = 0;

            ICompressionStrategy compressionStrategy = compressionServiceFactory.Create(
                compression
            );
            using Stream decompressedStream = await compressionStrategy.DecompressAsync(
                decryptedBuffer
            );
            using MemoryStream resultBuffer = new();
            await decompressedStream.CopyToAsync(resultBuffer, BufferSize);
            return resultBuffer.ToArray();
        }
        catch (InvalidCipherTextException)
        {
            throw new EncryptionInvalidPasswordException();
        }
        catch (CryptographicException)
        {
            throw new EncryptionInvalidPasswordException();
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new EncryptionAccessDeniedException(sourceFilePath, ex);
        }
        catch (EndOfStreamException)
        {
            throw new EncryptionCorruptedFileException(sourceFilePath);
        }
        catch (IOException ex)
        {
            throw new EncryptionCipherException(Messages.OperationDecryption, ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    protected abstract Task EncryptStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        byte[] key,
        byte[] nonce
    );

    protected abstract Task DecryptStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        byte[] key,
        byte[] nonce
    );

    protected static byte[] GenerateSalt()
    {
        byte[] salt = new byte[SaltSize];
        RandomNumberGenerator.Fill(salt);
        return salt;
    }

    protected static byte[] GenerateNonce()
    {
        byte[] nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);
        return nonce;
    }

    protected byte[] DeriveKey(
        string password,
        byte[] salt,
        int keySize,
        KeyDerivationAlgorithm algorithm
    )
    {
        IKeyDerivationAlgorithmStrategy keyDerivationService = keyDerivationServiceFactory.Create(
            algorithm
        );
        return keyDerivationService.DeriveKey(password, salt, keySize);
    }

    protected static async Task WriteSaltAsync(Stream stream, byte[] salt)
    {
        await stream.WriteAsync(salt);
    }

    protected static async Task<byte[]> ReadSaltAsync(Stream stream)
    {
        byte[] salt = new byte[SaltSize];
        await stream.ReadExactlyAsync(salt);
        return salt;
    }

    protected static async Task WriteNonceAsync(Stream stream, byte[] nonce)
    {
        await stream.WriteAsync(nonce);
    }

    protected static async Task<byte[]> ReadNonceAsync(Stream stream)
    {
        byte[] nonce = new byte[NonceSize];
        await stream.ReadExactlyAsync(nonce);
        return nonce;
    }

    protected static void WriteCompressionHeader(
        Stream stream,
        CompressionMode compression
    )
    {
        stream.WriteByte((byte)compression);
    }

    protected static CompressionMode ReadCompressionHeader(Stream stream)
    {
        int value = stream.ReadByte();
        if (value < 0)
        {
            throw new EndOfStreamException();
        }
        return (CompressionMode)value;
    }

    protected static async Task ProcessFileWithCipherAsync(
        Stream sourceStream,
        Stream destinationStream,
        IAeadCipher cipher
    )
    {
        byte[] inputBuffer = new byte[BufferSize];
        byte[] outputBuffer = new byte[BufferSize + MacSize / 8];

        int bytesRead;
        while ((bytesRead = await sourceStream.ReadAsync(inputBuffer)) > 0)
        {
            int processed = cipher.ProcessBytes(inputBuffer, 0, bytesRead, outputBuffer, 0);
            if (processed > 0)
            {
                await destinationStream.WriteAsync(outputBuffer.AsMemory(0, processed));
            }
        }

        int finalBytes = cipher.DoFinal(outputBuffer, 0);
        if (finalBytes > 0)
        {
            await destinationStream.WriteAsync(outputBuffer.AsMemory(0, finalBytes));
        }
    }

    private static void ValidateDiskSpace(
        string sourceFilePath,
        string destinationFilePath
    )
    {
        try
        {
            FileInfo sourceFileInfo = new(sourceFilePath);
            string? destinationDrive = Path.GetPathRoot(Path.GetFullPath(destinationFilePath));

            if (!string.IsNullOrEmpty(destinationDrive))
            {
                DriveInfo driveInfo = new(destinationDrive);
                if (driveInfo.IsReady)
                {
                    long requiredSpace = (long)(sourceFileInfo.Length * 1.2) + 1024;

                    if (driveInfo.AvailableFreeSpace < requiredSpace)
                    {
                        throw new EncryptionInsufficientSpaceException(destinationFilePath);
                    }
                }
            }
        }
        catch (EncryptionException)
        {
            throw;
        }
        catch
        {
            // If we can't check disk space, continue anyway
        }
    }

    private static void EnsureDirectoryExists(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new EncryptionAccessDeniedException(filePath, ex);
            }
        }
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        { /* ignore */
        }
    }
}
