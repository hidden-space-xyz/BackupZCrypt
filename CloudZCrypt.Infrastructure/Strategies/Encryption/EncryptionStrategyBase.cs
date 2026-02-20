using System.Buffers;
using System.Security.Cryptography;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Exceptions;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Services.Interfaces;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Infrastructure.Resources;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Modes;

namespace CloudZCrypt.Infrastructure.Strategies.Encryption;

internal abstract class EncryptionStrategyBase(
    IKeyDerivationServiceFactory keyDerivationServiceFactory,
    ICompressionServiceFactory compressionServiceFactory,
    IFileOperationsService fileOperationsService,
    ISystemStorageService systemStorageService
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
        CompressionMode compression = CompressionMode.None,
        CancellationToken cancellationToken = default
    )
    {
        byte[] salt = [];
        byte[] nonce = [];
        byte[] key = [];

        try
        {
            using Stream sourceFile = OpenSourceFile(sourceFilePath);

            EnsureDirectoryExists(destinationFilePath);
            ValidateDiskSpace(sourceFilePath, destinationFilePath);

            salt = GenerateSalt();
            nonce = GenerateNonce();
            key = DeriveKeySafe(password, salt, keyDerivationAlgorithm);

            using Stream destinationFile = fileOperationsService.CreateWriteStream(destinationFilePath, BufferSize);

            byte[] associatedData = BuildAssociatedData(salt, nonce, compression);
            await destinationFile.WriteAsync(associatedData, cancellationToken);

            ICompressionStrategy compressionStrategy = compressionServiceFactory.Create(
                compression
            );
            using Stream compressedSource = await compressionStrategy.CompressAsync(
                sourceFile,
                cancellationToken
            );
            await EncryptStreamAsync(
                compressedSource,
                destinationFile,
                key,
                nonce,
                associatedData,
                cancellationToken
            );

            return true;
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
    }

    public async Task<bool> DecryptFileAsync(
        string sourceFilePath,
        string destinationFilePath,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CancellationToken cancellationToken = default
    )
    {
        byte[] salt = [];
        byte[] nonce = [];
        byte[] key = [];

        try
        {
            using Stream sourceFile = OpenSourceFile(sourceFilePath, validateHeader: true);

            EnsureDirectoryExists(destinationFilePath);

            salt = await ReadSaltAsync(sourceFile, cancellationToken);
            nonce = await ReadNonceAsync(sourceFile, cancellationToken);
            CompressionMode compression = ReadCompressionHeader(sourceFile);
            byte[] associatedData = BuildAssociatedData(salt, nonce, compression);

            key = DeriveKeySafe(password, salt, keyDerivationAlgorithm);

            using Stream decryptedBuffer = fileOperationsService.CreateTempStream(BufferSize);
            await DecryptStreamAsync(
                sourceFile,
                decryptedBuffer,
                key,
                nonce,
                associatedData,
                cancellationToken
            );
            decryptedBuffer.Position = 0;

            ICompressionStrategy compressionStrategy = compressionServiceFactory.Create(
                compression
            );
            using Stream decompressedStream = await compressionStrategy.DecompressAsync(
                decryptedBuffer,
                cancellationToken
            );

            using Stream destinationFile = fileOperationsService.CreateWriteStream(destinationFilePath, BufferSize);
            await decompressedStream.CopyToAsync(destinationFile, BufferSize, cancellationToken);

            return true;
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
    }

    public virtual async Task<bool> CreateEncryptedFileAsync(
        byte[] plaintextData,
        string destinationFilePath,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CompressionMode compression = CompressionMode.None,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(plaintextData);

        EnsureDirectoryExists(destinationFilePath);

        byte[] salt = GenerateSalt();
        byte[] nonce = GenerateNonce();
        byte[] key = [];

        try
        {
            key = DeriveKeySafe(password, salt, keyDerivationAlgorithm);

            using Stream destinationFile = fileOperationsService.CreateWriteStream(destinationFilePath, BufferSize);
            byte[] associatedData = BuildAssociatedData(salt, nonce, compression);
            await destinationFile.WriteAsync(associatedData, cancellationToken);

            using Stream source = new MemoryStream(plaintextData, writable: false);
            ICompressionStrategy compressionStrategy = compressionServiceFactory.Create(
                compression
            );
            using Stream compressedSource = await compressionStrategy.CompressAsync(
                source,
                cancellationToken
            );
            await EncryptStreamAsync(
                compressedSource,
                destinationFile,
                key,
                nonce,
                associatedData,
                cancellationToken
            );
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
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CancellationToken cancellationToken = default
    )
    {
        byte[] salt = [];
        byte[] nonce = [];
        byte[] key = [];

        try
        {
            using Stream source = OpenSourceFile(sourceFilePath, validateHeader: true);

            salt = await ReadSaltAsync(source, cancellationToken);
            nonce = await ReadNonceAsync(source, cancellationToken);
            CompressionMode compression = ReadCompressionHeader(source);
            byte[] associatedData = BuildAssociatedData(salt, nonce, compression);

            key = DeriveKeySafe(password, salt, keyDerivationAlgorithm);

            using Stream decryptedBuffer = fileOperationsService.CreateTempStream(BufferSize);
            await DecryptStreamAsync(
                source,
                decryptedBuffer,
                key,
                nonce,
                associatedData,
                cancellationToken
            );
            decryptedBuffer.Position = 0;

            ICompressionStrategy compressionStrategy = compressionServiceFactory.Create(
                compression
            );
            using Stream decompressedStream = await compressionStrategy.DecompressAsync(
                decryptedBuffer,
                cancellationToken
            );
            using MemoryStream resultBuffer = new();
            await decompressedStream.CopyToAsync(resultBuffer, BufferSize, cancellationToken);
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
        byte[] nonce,
        byte[] associatedData,
        CancellationToken cancellationToken
    );

    protected abstract Task DecryptStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        byte[] key,
        byte[] nonce,
        byte[] associatedData,
        CancellationToken cancellationToken
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

    protected static async Task<byte[]> ReadSaltAsync(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        byte[] salt = new byte[SaltSize];
        await stream.ReadExactlyAsync(salt, cancellationToken);
        return salt;
    }

    protected static async Task<byte[]> ReadNonceAsync(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        byte[] nonce = new byte[NonceSize];
        await stream.ReadExactlyAsync(nonce, cancellationToken);
        return nonce;
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
        IAeadCipher cipher,
        CancellationToken cancellationToken
    )
    {
        byte[] inputBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        byte[] outputBuffer = ArrayPool<byte>.Shared.Rent(BufferSize + MacSize / 8);

        try
        {
            int bytesRead;
            while (
                (bytesRead = await sourceStream.ReadAsync(inputBuffer, cancellationToken)) > 0
            )
            {
                int processed = cipher.ProcessBytes(inputBuffer, 0, bytesRead, outputBuffer, 0);
                if (processed > 0)
                {
                    await destinationStream.WriteAsync(
                        outputBuffer.AsMemory(0, processed),
                        cancellationToken
                    );
                }
            }

            int finalBytes = cipher.DoFinal(outputBuffer, 0);
            if (finalBytes > 0)
            {
                await destinationStream.WriteAsync(
                    outputBuffer.AsMemory(0, finalBytes),
                    cancellationToken
                );
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(inputBuffer, clearArray: true);
            ArrayPool<byte>.Shared.Return(outputBuffer, clearArray: true);
        }
    }

    private static byte[] BuildAssociatedData(
        byte[] salt,
        byte[] nonce,
        CompressionMode compression
    )
    {
        byte[] header = new byte[HeaderSize];
        Buffer.BlockCopy(salt, 0, header, 0, SaltSize);
        Buffer.BlockCopy(nonce, 0, header, SaltSize, NonceSize);
        header[SaltSize + NonceSize] = (byte)compression;
        return header;
    }

    private Stream OpenSourceFile(string sourceFilePath, bool validateHeader = false)
    {
        if (!fileOperationsService.FileExists(sourceFilePath))
        {
            throw new EncryptionFileNotFoundException(sourceFilePath);
        }

        Stream stream;
        try
        {
            stream = fileOperationsService.OpenReadStream(sourceFilePath, BufferSize);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new EncryptionAccessDeniedException(sourceFilePath, ex);
        }

        if (validateHeader && stream.Length < HeaderSize)
        {
            stream.Dispose();
            throw new EncryptionCorruptedFileException(sourceFilePath);
        }

        return stream;
    }

    private byte[] DeriveKeySafe(
        string password,
        byte[] salt,
        KeyDerivationAlgorithm algorithm
    )
    {
        try
        {
            return DeriveKey(password, salt, KeySize, algorithm);
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

    private void ValidateDiskSpace(string sourceFilePath, string destinationFilePath)
    {
        try
        {
            long sourceLength = fileOperationsService.GetFileSize(sourceFilePath);
            string fullPath = fileOperationsService.GetFullPath(destinationFilePath);
            string? destinationDrive = systemStorageService.GetPathRoot(fullPath);

            if (!string.IsNullOrEmpty(destinationDrive))
            {
                long availableSpace = systemStorageService.GetAvailableFreeSpace(destinationDrive);

                if (availableSpace >= 0)
                {
                    long requiredSpace = (long)(sourceLength * 1.2) + 1024;

                    if (availableSpace < requiredSpace)
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

    private void EnsureDirectoryExists(string filePath)
    {
        string? directory = fileOperationsService.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            try
            {
                fileOperationsService.CreateDirectory(directory);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new EncryptionAccessDeniedException(filePath, ex);
            }
        }
    }

    private void TryDeleteFile(string filePath)
    {
        try
        {
            if (fileOperationsService.FileExists(filePath))
            {
                fileOperationsService.DeleteFile(filePath);
            }
        }
        catch
        { /* ignore */
        }
    }
}
