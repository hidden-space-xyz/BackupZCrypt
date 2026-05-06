namespace BackupZCrypt.Infrastructure.Strategies.Encryption;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Encryption;
using BackupZCrypt.Infrastructure.Constants;
using BackupZCrypt.Infrastructure.Resources;
using DomainMessages = BackupZCrypt.Domain.Resources.Messages;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Modes;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

internal abstract class EncryptionStrategyBase(
    IEncryptionSessionFactory encryptionSessionFactory,
    ICompressionServiceFactory compressionServiceFactory,
    IEncryptionFileService encryptionFileService)
{
    protected const int MacSize = EncryptionConstants.MacSize;
    protected const int MacSizeBytes = EncryptionConstants.MacSize / 8;
    protected const int BufferSize = EncryptionConstants.BufferSize;

    public async Task<EncryptionResult<EncryptionMetadata>> EncryptFileAsync(
        string sourceFilePath,
        string destinationFilePath,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CompressionMode compression = CompressionMode.None,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var sourceFile = encryptionFileService.OpenSourceFile(sourceFilePath);

            encryptionFileService.EnsureDirectoryExists(destinationFilePath);
            encryptionFileService.ValidateDiskSpace(sourceFilePath, destinationFilePath);

            using var session = encryptionSessionFactory.CreateEncryptionSession(
                password,
                keyDerivationAlgorithm,
                compression);

            await using var destinationFile = encryptionFileService.CreateWriteStream(
                destinationFilePath);

            if (compression != CompressionMode.None)
            {
                var compressionStrategy = compressionServiceFactory.Create(
                    compression);
                await using var compressedSource = await compressionStrategy.CompressAsync(
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

            return EncryptionResult<EncryptionMetadata>.Success(
                new EncryptionMetadata(
                    (byte[])session.Salt.Clone(),
                    (byte[])session.Nonce.Clone(),
                    compression));
        }
        catch (InvalidDataException)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);

            return EncryptionResult<EncryptionMetadata>.Failure(
                BackupErrorCode.FileCorruption,
                string.Format(DomainMessages.CorruptedFileFormat, sourceFilePath));
        }
        catch (IOException ex)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);

            return ex.Message.Contains("space", StringComparison.OrdinalIgnoreCase)
                ? EncryptionResult<EncryptionMetadata>.Failure(
                    BackupErrorCode.InsufficientDiskSpace,
                    string.Format(DomainMessages.InsufficientDiskSpaceFormat, destinationFilePath))
                : EncryptionResult<EncryptionMetadata>.Failure(
                    BackupErrorCode.CipherOperationFailed,
                    string.Format(DomainMessages.CipherOperationFailedFormat, Messages.OperationEncryption));
        }
        catch (UnauthorizedAccessException)
        {
            return EncryptionResult<EncryptionMetadata>.Failure(
                BackupErrorCode.AccessDenied,
                string.Format(DomainMessages.AccessDeniedFormat, destinationFilePath));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);

            return EncryptionResult<EncryptionMetadata>.Failure(
                BackupErrorCode.CipherOperationFailed,
                string.Format(DomainMessages.CipherOperationFailedFormat, Messages.OperationEncryption));
        }
    }

    public async Task<EncryptionResult<bool>> DecryptFileAsync(
        string sourceFilePath,
        string destinationFilePath,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        EncryptionMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var sourceFile = encryptionFileService.OpenSourceFile(sourceFilePath);

            encryptionFileService.EnsureDirectoryExists(destinationFilePath);

            using var session = encryptionSessionFactory.CreateDecryptionSession(
                password,
                keyDerivationAlgorithm,
                metadata);

            await using var decryptedBuffer = encryptionFileService.CreateTempStream();
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
                var compressionStrategy = compressionServiceFactory.Create(
                    session.Compression);
                await using var decompressedStream = await compressionStrategy.DecompressAsync(
                    decryptedBuffer,
                    cancellationToken);

                await using var destinationFile = encryptionFileService.CreateWriteStream(
                    destinationFilePath);
                await decompressedStream.CopyToAsync(
                    destinationFile,
                    BufferSize,
                    cancellationToken);
            }
            else
            {
                await using var destinationFile = encryptionFileService.CreateWriteStream(
                    destinationFilePath);
                await decryptedBuffer.CopyToAsync(
                    destinationFile,
                    BufferSize,
                    cancellationToken);
            }

            return EncryptionResult<bool>.Success(true);
        }
        catch (InvalidCipherTextException)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);
            return EncryptionResult<bool>.Failure(
                BackupErrorCode.InvalidPassword,
                DomainMessages.InvalidPassword);
        }
        catch (CryptographicException)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);
            return EncryptionResult<bool>.Failure(
                BackupErrorCode.InvalidPassword,
                DomainMessages.InvalidPassword);
        }
        catch (EndOfStreamException)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);
            return EncryptionResult<bool>.Failure(
                BackupErrorCode.FileCorruption,
                string.Format(DomainMessages.CorruptedFileFormat, sourceFilePath));
        }
        catch (InvalidDataException)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);
            return EncryptionResult<bool>.Failure(
                BackupErrorCode.FileCorruption,
                string.Format(DomainMessages.CorruptedFileFormat, sourceFilePath));
        }
        catch (IOException ex)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);

            return ex.Message.Contains("space", StringComparison.OrdinalIgnoreCase)
                ? EncryptionResult<bool>.Failure(
                    BackupErrorCode.InsufficientDiskSpace,
                    string.Format(DomainMessages.InsufficientDiskSpaceFormat, destinationFilePath))
                : EncryptionResult<bool>.Failure(
                    BackupErrorCode.CipherOperationFailed,
                    string.Format(DomainMessages.CipherOperationFailedFormat, Messages.OperationDecryption));
        }
        catch (UnauthorizedAccessException)
        {
            return EncryptionResult<bool>.Failure(
                BackupErrorCode.AccessDenied,
                string.Format(DomainMessages.AccessDeniedFormat, destinationFilePath));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);

            return EncryptionResult<bool>.Failure(
                BackupErrorCode.CipherOperationFailed,
                string.Format(DomainMessages.CipherOperationFailedFormat, Messages.OperationDecryption));
        }
    }

    public async Task<EncryptionResult<bool>> DecryptFileAsync(
        string sourceFilePath,
        string destinationFilePath,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var sourceFile = encryptionFileService.OpenSourceFile(
                sourceFilePath,
                validateHeader: true);

            encryptionFileService.EnsureDirectoryExists(destinationFilePath);

            using var session =
                await encryptionSessionFactory.CreateDecryptionSessionAsync(
                    sourceFile,
                    password,
                    keyDerivationAlgorithm,
                    cancellationToken);

            await using var decryptedBuffer = encryptionFileService.CreateTempStream();
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
                var compressionStrategy = compressionServiceFactory.Create(
                    session.Compression);
                await using var decompressedStream = await compressionStrategy.DecompressAsync(
                    decryptedBuffer,
                    cancellationToken);

                await using var destinationFile = encryptionFileService.CreateWriteStream(
                    destinationFilePath);
                await decompressedStream.CopyToAsync(
                    destinationFile,
                    BufferSize,
                    cancellationToken);
            }
            else
            {
                await using var destinationFile = encryptionFileService.CreateWriteStream(
                    destinationFilePath);
                await decryptedBuffer.CopyToAsync(
                    destinationFile,
                    BufferSize,
                    cancellationToken);
            }

            return EncryptionResult<bool>.Success(true);
        }
        catch (InvalidCipherTextException)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);
            return EncryptionResult<bool>.Failure(
                BackupErrorCode.InvalidPassword,
                DomainMessages.InvalidPassword);
        }
        catch (CryptographicException)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);
            return EncryptionResult<bool>.Failure(
                BackupErrorCode.InvalidPassword,
                DomainMessages.InvalidPassword);
        }
        catch (EndOfStreamException)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);
            return EncryptionResult<bool>.Failure(
                BackupErrorCode.FileCorruption,
                string.Format(DomainMessages.CorruptedFileFormat, sourceFilePath));
        }
        catch (InvalidDataException)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);
            return EncryptionResult<bool>.Failure(
                BackupErrorCode.FileCorruption,
                string.Format(DomainMessages.CorruptedFileFormat, sourceFilePath));
        }
        catch (IOException ex)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);

            return ex.Message.Contains("space", StringComparison.OrdinalIgnoreCase)
                ? EncryptionResult<bool>.Failure(
                    BackupErrorCode.InsufficientDiskSpace,
                    string.Format(DomainMessages.InsufficientDiskSpaceFormat, destinationFilePath))
                : EncryptionResult<bool>.Failure(
                    BackupErrorCode.CipherOperationFailed,
                    string.Format(DomainMessages.CipherOperationFailedFormat, Messages.OperationDecryption));
        }
        catch (UnauthorizedAccessException)
        {
            return EncryptionResult<bool>.Failure(
                BackupErrorCode.AccessDenied,
                string.Format(DomainMessages.AccessDeniedFormat, destinationFilePath));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            encryptionFileService.TryDeleteFile(destinationFilePath);

            return EncryptionResult<bool>.Failure(
                BackupErrorCode.CipherOperationFailed,
                string.Format(DomainMessages.CipherOperationFailedFormat, Messages.OperationDecryption));
        }
    }

    public virtual async Task<byte[]> CreateEncryptedDataAsync(
        byte[] plaintextData,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CompressionMode compression = CompressionMode.None,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plaintextData);

        using var session = encryptionSessionFactory.CreateEncryptionSession(
            password,
            keyDerivationAlgorithm,
            compression);

        await using MemoryStream destinationBuffer = new();
        await destinationBuffer.WriteAsync(session.AssociatedData, cancellationToken);

        await using Stream source = new MemoryStream(plaintextData, writable: false);

        if (compression != CompressionMode.None)
        {
            var compressionStrategy = compressionServiceFactory.Create(
                compression);
            await using var compressedSource = await compressionStrategy.CompressAsync(
                source,
                cancellationToken);
            await EncryptStreamAsync(
                compressedSource,
                destinationBuffer,
                session.Key,
                session.Nonce,
                session.AssociatedData,
                cancellationToken);
        }
        else
        {
            await EncryptStreamAsync(
                source,
                destinationBuffer,
                session.Key,
                session.Nonce,
                session.AssociatedData,
                cancellationToken);
        }

        return destinationBuffer.ToArray();
    }

    public virtual async Task<byte[]> ReadEncryptedDataAsync(
        ReadOnlyMemory<byte> encryptedData,
        string password,
        KeyDerivationAlgorithm keyDerivationAlgorithm,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var source = CreateReadOnlyMemoryStream(encryptedData);

            using var session =
                await encryptionSessionFactory.CreateDecryptionSessionAsync(
                    source,
                    password,
                    keyDerivationAlgorithm,
                    cancellationToken);

            await using var decryptedBuffer = encryptionFileService.CreateTempStream();
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
                var compressionStrategy = compressionServiceFactory.Create(
                    session.Compression);
                await using var decompressedStream = await compressionStrategy.DecompressAsync(
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
            throw new CryptographicException(DomainMessages.InvalidPassword);
        }
        catch (CryptographicException)
        {
            throw new CryptographicException(DomainMessages.InvalidPassword);
        }
        catch (IOException ex)
        {
            throw new IOException(
                string.Format(DomainMessages.CipherOperationFailedFormat, Messages.OperationDecryption), ex);
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
        IAeadCipher cipher,
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
        var inputBuffer = ArrayPool<byte>.Shared.Rent(EncryptionConstants.BufferSize);
        var outputBuffer = ArrayPool<byte>.Shared.Rent(
            EncryptionConstants.BufferSize + (EncryptionConstants.MacSize / 8));

        try
        {
            int bytesRead;
            while ((bytesRead = await sourceStream.ReadAsync(inputBuffer, cancellationToken)) > 0)
            {
                var processed = cipher.ProcessBytes(inputBuffer, 0, bytesRead, outputBuffer, 0);
                if (processed > 0)
                {
                    await destinationStream.WriteAsync(
                        outputBuffer.AsMemory(0, processed),
                        cancellationToken);
                }
            }

            var finalBytes = cipher.DoFinal(outputBuffer, 0);
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

    protected static async Task<byte[]> ReadAllBytesAsync(
        Stream sourceStream,
        CancellationToken cancellationToken)
    {
        if (sourceStream.CanSeek)
        {
            var remainingLength = sourceStream.Length - sourceStream.Position;
            if (remainingLength == 0)
            {
                return (byte[])[];
            }

            if (remainingLength > 0 && remainingLength <= int.MaxValue)
            {
                var dataBuffer = new byte[(int)remainingLength];
                var totalRead = 0;

                while (totalRead < dataBuffer.Length)
                {
                    var bytesRead = await sourceStream.ReadAsync(
                        dataBuffer.AsMemory(totalRead),
                        cancellationToken);

                    if (bytesRead == 0)
                    {
                        break;
                    }

                    totalRead += bytesRead;
                }

                if (totalRead == dataBuffer.Length)
                {
                    return dataBuffer;
                }

                var trimmedBuffer = new byte[totalRead];
                dataBuffer.AsSpan(0, totalRead).CopyTo(trimmedBuffer);
                CryptographicOperations.ZeroMemory(dataBuffer);
                return trimmedBuffer;
            }
        }

        await using MemoryStream bufferStream = new();
        await sourceStream.CopyToAsync(bufferStream, BufferSize, cancellationToken);
        return bufferStream.ToArray();
    }

    private static MemoryStream CreateReadOnlyMemoryStream(ReadOnlyMemory<byte> data)
    {
        if (MemoryMarshal.TryGetArray(data, out var segment)
            && segment.Array is not null)
        {
            return new MemoryStream(
                segment.Array,
                segment.Offset,
                segment.Count,
                writable: false,
                publiclyVisible: false);
        }

        return new MemoryStream(data.ToArray(), writable: false);
    }
}
