namespace BackupZCrypt.Infrastructure.Strategies.Encryption;

using System.Buffers;
using System.Security.Cryptography;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Exceptions;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Encryption;
using BackupZCrypt.Infrastructure.Constants;
using BackupZCrypt.Infrastructure.Resources;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Modes;

internal abstract class EncryptionStrategyBase(
    IEncryptionSessionFactory encryptionSessionFactory,
    ICompressionServiceFactory compressionServiceFactory,
    IEncryptionFileService encryptionFileService)
{
    protected const int MacSize = EncryptionConstants.MacSize;
    protected const int BufferSize = EncryptionConstants.BufferSize;

    public async Task<bool> EncryptFileAsync(
        string sourceFilePath,
        string destinationFilePath,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CompressionMode compression = CompressionMode.None,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using Stream sourceFile = encryptionFileService.OpenSourceFile(sourceFilePath);

            encryptionFileService.EnsureDirectoryExists(destinationFilePath);
            encryptionFileService.ValidateDiskSpace(sourceFilePath, destinationFilePath);

            using EncryptionSession session = encryptionSessionFactory.CreateEncryptionSession(
                password,
                keyDerivationAlgorithm,
                compression);

            await using Stream destinationFile = encryptionFileService.CreateWriteStream(
                destinationFilePath);

            await destinationFile.WriteAsync(session.AssociatedData, cancellationToken);

            if (compression != CompressionMode.None)
            {
                ICompressionStrategy compressionStrategy = compressionServiceFactory.Create(
                    compression);
                await using Stream compressedSource = await compressionStrategy.CompressAsync(
                    sourceFile,
                    cancellationToken);
                await EncryptStreamAsync(
                    compressedSource,
                    destinationFile,
                    session.Key,
                    session.Nonce,
                    session.AssociatedData,
                    cancellationToken);
            }
            else
            {
                await EncryptStreamAsync(
                    sourceFile,
                    destinationFile,
                    session.Key,
                    session.Nonce,
                    session.AssociatedData,
                    cancellationToken);
            }

            return true;
        }
        catch (EncryptionException)
        {
            throw;
        }
        catch (IOException ex)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);

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
            encryptionFileService.TryDeleteFile(destinationFilePath);

            throw new EncryptionCipherException(Messages.OperationEncryption, ex);
        }
    }

    public async Task<bool> DecryptFileAsync(
        string sourceFilePath,
        string destinationFilePath,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using Stream sourceFile = encryptionFileService.OpenSourceFile(
                sourceFilePath,
                validateHeader: true);

            encryptionFileService.EnsureDirectoryExists(destinationFilePath);

            using EncryptionSession session =
                await encryptionSessionFactory.CreateDecryptionSessionAsync(
                    sourceFile,
                    password,
                    keyDerivationAlgorithm,
                    cancellationToken);

            await using Stream decryptedBuffer = encryptionFileService.CreateTempStream();
            await DecryptStreamAsync(
                sourceFile,
                decryptedBuffer,
                session.Key,
                session.Nonce,
                session.AssociatedData,
                cancellationToken);
            decryptedBuffer.Position = 0;

            if (session.Compression != CompressionMode.None)
            {
                ICompressionStrategy compressionStrategy = compressionServiceFactory.Create(
                    session.Compression);
                await using Stream decompressedStream = await compressionStrategy.DecompressAsync(
                    decryptedBuffer,
                    cancellationToken);

                await using Stream destinationFile = encryptionFileService.CreateWriteStream(
                    destinationFilePath);
                await decompressedStream.CopyToAsync(
                    destinationFile,
                    BufferSize,
                    cancellationToken);
            }
            else
            {
                await using Stream destinationFile = encryptionFileService.CreateWriteStream(
                    destinationFilePath);
                await decryptedBuffer.CopyToAsync(
                    destinationFile,
                    BufferSize,
                    cancellationToken);
            }

            return true;
        }
        catch (EncryptionException)
        {
            throw;
        }
        catch (InvalidCipherTextException)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);
            throw new EncryptionInvalidPasswordException();
        }
        catch (CryptographicException)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);
            throw new EncryptionInvalidPasswordException();
        }
        catch (EndOfStreamException)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);
            throw new EncryptionCorruptedFileException(sourceFilePath);
        }
        catch (IOException ex)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);

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
            encryptionFileService.TryDeleteFile(destinationFilePath);

            throw new EncryptionCipherException(Messages.OperationDecryption, ex);
        }
    }

    public virtual async Task<bool> CreateEncryptedFileAsync(
        byte[] plaintextData,
        string destinationFilePath,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CompressionMode compression = CompressionMode.None,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plaintextData);

        encryptionFileService.EnsureDirectoryExists(destinationFilePath);

        try
        {
            using EncryptionSession session = encryptionSessionFactory.CreateEncryptionSession(
                password,
                keyDerivationAlgorithm,
                compression);

            await using Stream destinationFile = encryptionFileService.CreateWriteStream(
                destinationFilePath);
            await destinationFile.WriteAsync(session.AssociatedData, cancellationToken);

            await using Stream source = new MemoryStream(plaintextData, writable: false);

            if (compression != CompressionMode.None)
            {
                ICompressionStrategy compressionStrategy = compressionServiceFactory.Create(
                    compression);
                await using Stream compressedSource = await compressionStrategy.CompressAsync(
                    source,
                    cancellationToken);
                await EncryptStreamAsync(
                    compressedSource,
                    destinationFile,
                    session.Key,
                    session.Nonce,
                    session.AssociatedData,
                    cancellationToken);
            }
            else
            {
                await EncryptStreamAsync(
                    source,
                    destinationFile,
                    session.Key,
                    session.Nonce,
                    session.AssociatedData,
                    cancellationToken);
            }
        }
        catch (EncryptionException)
        {
            throw;
        }
        catch (IOException ex)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);

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
            encryptionFileService.TryDeleteFile(destinationFilePath);

            throw new EncryptionCipherException(Messages.OperationEncryption, ex);
        }

        return true;
    }

    public virtual async Task<byte[]> ReadEncryptedFileAsync(
        string sourceFilePath,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using Stream source = encryptionFileService.OpenSourceFile(
                sourceFilePath,
                validateHeader: true);

            using EncryptionSession session =
                await encryptionSessionFactory.CreateDecryptionSessionAsync(
                    source,
                    password,
                    keyDerivationAlgorithm,
                    cancellationToken);

            await using Stream decryptedBuffer = encryptionFileService.CreateTempStream();
            await DecryptStreamAsync(
                source,
                decryptedBuffer,
                session.Key,
                session.Nonce,
                session.AssociatedData,
                cancellationToken);
            decryptedBuffer.Position = 0;

            await using MemoryStream resultBuffer = new();

            if (session.Compression != CompressionMode.None)
            {
                ICompressionStrategy compressionStrategy = compressionServiceFactory.Create(
                    session.Compression);
                await using Stream decompressedStream = await compressionStrategy.DecompressAsync(
                    decryptedBuffer,
                    cancellationToken);
                await decompressedStream.CopyToAsync(resultBuffer, BufferSize, cancellationToken);
            }
            else
            {
                await decryptedBuffer.CopyToAsync(resultBuffer, BufferSize, cancellationToken);
            }
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
    }

    protected abstract Task EncryptStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        byte[] key,
        byte[] nonce,
        byte[] associatedData,
        CancellationToken cancellationToken);

    protected abstract Task DecryptStreamAsync(
        Stream sourceStream,
        Stream destinationStream,
        byte[] key,
        byte[] nonce,
        byte[] associatedData,
        CancellationToken cancellationToken);

    protected static Task ProcessFileWithCipherAsync(
        Stream sourceStream,
        Stream destinationStream,
        Org.BouncyCastle.Crypto.Modes.IAeadCipher cipher,
        CancellationToken cancellationToken)
    {
        return ProcessAsync(sourceStream, destinationStream, cipher, cancellationToken);
    }

    private static async Task ProcessAsync(
        Stream sourceStream,
        Stream destinationStream,
        IAeadCipher cipher,
        CancellationToken cancellationToken)
    {
        byte[] inputBuffer = ArrayPool<byte>.Shared.Rent(EncryptionConstants.BufferSize);
        byte[] outputBuffer = ArrayPool<byte>.Shared.Rent(
            EncryptionConstants.BufferSize + (EncryptionConstants.MacSize / 8));

        try
        {
            int bytesRead;
            while ((bytesRead = await sourceStream.ReadAsync(inputBuffer, cancellationToken)) > 0)
            {
                int processed = cipher.ProcessBytes(inputBuffer, 0, bytesRead, outputBuffer, 0);
                if (processed > 0)
                {
                    await destinationStream.WriteAsync(
                        outputBuffer.AsMemory(0, processed),
                        cancellationToken);
                }
            }

            int finalBytes = cipher.DoFinal(outputBuffer, 0);
            if (finalBytes > 0)
            {
                await destinationStream.WriteAsync(
                    outputBuffer.AsMemory(0, finalBytes),
                    cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(inputBuffer, clearArray: true);
            ArrayPool<byte>.Shared.Return(outputBuffer, clearArray: true);
        }
    }
}
