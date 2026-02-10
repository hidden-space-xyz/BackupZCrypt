using System.Security.Cryptography;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Exceptions;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Strategies.Interfaces;
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
    protected const int BufferSize = 4 * 1024;

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

            string? destinationDir = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrEmpty(destinationDir))
            {
                try
                {
                    Directory.CreateDirectory(destinationDir);
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new EncryptionAccessDeniedException(destinationFilePath, ex);
                }
            }

            await ValidateDiskSpaceAsync(sourceFilePath, destinationFilePath);

            byte[] salt = GenerateSalt();
            byte[] nonce = GenerateNonce();

            byte[] key;
            try
            {
                key = DeriveKey(password, salt, KeySize, keyDerivationAlgorithm);
            }
            catch (Exception ex)
            {
                throw new EncryptionKeyDerivationException(ex);
            }

            try
            {
                using Stream sourceFile = File.OpenRead(sourceFilePath);
                using Stream destinationFile = File.Create(destinationFilePath);

                await WriteSaltAsync(destinationFile, salt);
                await WriteNonceAsync(destinationFile, nonce);
                await WriteCompressionHeaderAsync(destinationFile, compression);

                ICompressionStrategy compressionStrategy = compressionServiceFactory.Create(
                    compression
                );
                using Stream compressedSource = await compressionStrategy.CompressAsync(sourceFile);
                await EncryptStreamAsync(compressedSource, destinationFile, key, nonce);
            }
            catch (IOException ex)
            {
                try
                {
                    if (File.Exists(destinationFilePath))
                    {
                        File.Delete(destinationFilePath);
                    }
                }
                catch
                { /* ignore */
                }

                if (ex.Message.Contains("space", StringComparison.OrdinalIgnoreCase))
                {
                    throw new EncryptionInsufficientSpaceException(destinationFilePath);
                }
                throw new EncryptionCipherException("encryption", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new EncryptionAccessDeniedException(destinationFilePath, ex);
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(destinationFilePath))
                    {
                        File.Delete(destinationFilePath);
                    }
                }
                catch
                { /* ignore */
                }

                throw new EncryptionCipherException("encryption", ex);
            }

            return true;
        }
        catch (EncryptionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new EncryptionCipherException("encryption", ex);
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
            if (fileInfo.Length < SaltSize + NonceSize + CompressionHeaderSize)
            {
                throw new EncryptionCorruptedFileException(sourceFilePath);
            }

            string? destinationDir = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrEmpty(destinationDir))
            {
                try
                {
                    Directory.CreateDirectory(destinationDir);
                }
                catch (UnauthorizedAccessException ex)
                {
                    throw new EncryptionAccessDeniedException(destinationFilePath, ex);
                }
            }

            byte[] salt,
                nonce,
                key;
            CompressionMode compression;
            try
            {
                using Stream sourceFile = File.OpenRead(sourceFilePath);
                salt = await ReadSaltAsync(sourceFile);
                nonce = await ReadNonceAsync(sourceFile);
                compression = await ReadCompressionHeaderAsync(sourceFile);

                // Position is now right after salt+nonce+compression
            }
            catch (EndOfStreamException)
            {
                throw new EncryptionCorruptedFileException(sourceFilePath);
            }
            catch
            {
                throw new EncryptionCorruptedFileException(sourceFilePath);
            }

            try
            {
                key = DeriveKey(password, salt, KeySize, keyDerivationAlgorithm);
            }
            catch (Exception ex)
            {
                throw new EncryptionKeyDerivationException(ex);
            }

            try
            {
                using Stream sourceFile = File.OpenRead(sourceFilePath);

                // Skip salt + nonce + compression header to reach ciphertext start
                await sourceFile.ReadExactlyAsync(
                    new byte[SaltSize + NonceSize + CompressionHeaderSize]
                );

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
                await decompressedStream.CopyToAsync(destinationFile);
            }
            catch (IOException ex)
            {
                try
                {
                    if (File.Exists(destinationFilePath))
                    {
                        File.Delete(destinationFilePath);
                    }
                }
                catch
                { /* ignore */
                }

                if (ex.Message.Contains("space", StringComparison.OrdinalIgnoreCase))
                {
                    throw new EncryptionInsufficientSpaceException(destinationFilePath);
                }
                throw new EncryptionCipherException("decryption", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new EncryptionAccessDeniedException(destinationFilePath, ex);
            }
            catch (CryptographicException)
            {
                throw new EncryptionInvalidPasswordException();
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(destinationFilePath))
                    {
                        File.Delete(destinationFilePath);
                    }
                }
                catch
                { /* ignore */
                }

                if (
                    ex.Message.Contains("tag", StringComparison.OrdinalIgnoreCase)
                    || ex.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase)
                    || ex.Message.Contains("mac", StringComparison.OrdinalIgnoreCase)
                )
                {
                    throw new EncryptionInvalidPasswordException();
                }

                throw new EncryptionCipherException("decryption", ex);
            }

            return true;
        }
        catch (EncryptionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new EncryptionCipherException("decryption", ex);
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

        string? destinationDir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        byte[] salt = GenerateSalt();
        byte[] nonce = GenerateNonce();

        byte[] key;
        try
        {
            key = DeriveKey(password, salt, KeySize, keyDerivationAlgorithm);
        }
        catch (Exception ex)
        {
            throw new EncryptionKeyDerivationException(ex);
        }

        try
        {
            using Stream destinationFile = File.Create(destinationFilePath);
            await WriteSaltAsync(destinationFile, salt);
            await WriteNonceAsync(destinationFile, nonce);
            await WriteCompressionHeaderAsync(destinationFile, compression);

            using Stream source = new MemoryStream(plaintextData, writable: false);
            ICompressionStrategy compressionStrategy = compressionServiceFactory.Create(
                compression
            );
            using Stream compressedSource = await compressionStrategy.CompressAsync(source);
            await EncryptStreamAsync(compressedSource, destinationFile, key, nonce);
        }
        catch (IOException ex)
        {
            try
            {
                if (File.Exists(destinationFilePath))
                {
                    File.Delete(destinationFilePath);
                }
            }
            catch
            { /* ignore */
            }

            if (ex.Message.Contains("space", StringComparison.OrdinalIgnoreCase))
            {
                throw new EncryptionInsufficientSpaceException(destinationFilePath);
            }
            throw new EncryptionCipherException("encryption", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new EncryptionAccessDeniedException(destinationFilePath, ex);
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(destinationFilePath))
                {
                    File.Delete(destinationFilePath);
                }
            }
            catch
            { /* ignore */
            }

            throw new EncryptionCipherException("encryption", ex);
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

        try
        {
            using Stream source = File.OpenRead(sourceFilePath);

            if (source.Length < SaltSize + NonceSize + CompressionHeaderSize)
            {
                throw new EncryptionCorruptedFileException(sourceFilePath);
            }

            byte[] salt = await ReadSaltAsync(source);
            byte[] nonce = await ReadNonceAsync(source);
            CompressionMode compression = await ReadCompressionHeaderAsync(source);

            byte[] key;
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
            await decompressedStream.CopyToAsync(resultBuffer);
            return resultBuffer.ToArray();
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
            throw new EncryptionCipherException("decryption", ex);
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
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }
        return salt;
    }

    protected static byte[] GenerateNonce()
    {
        byte[] nonce = new byte[NonceSize];
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(nonce);
        }
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

    protected static async Task WriteCompressionHeaderAsync(
        Stream stream,
        CompressionMode compression
    )
    {
        await stream.WriteAsync(new[] { (byte)compression });
    }

    protected static async Task<CompressionMode> ReadCompressionHeaderAsync(Stream stream)
    {
        byte[] header = new byte[CompressionHeaderSize];
        await stream.ReadExactlyAsync(header);
        return (CompressionMode)header[0];
    }

    protected static async Task ProcessFileWithCipherAsync(
        Stream sourceStream,
        Stream destinationStream,
        IAeadCipher cipher
    )
    {
        byte[] inputBuffer = new byte[BufferSize];
        byte[] outputBuffer = new byte[BufferSize];

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

    private static async Task ValidateDiskSpaceAsync(
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

        await Task.CompletedTask;
    }
}
