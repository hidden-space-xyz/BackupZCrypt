namespace CloudZCrypt.Infrastructure.Constants;

internal static class EncryptionConstants
{
    internal const int KeySize = 256;
    internal const int SaltSize = 32;
    internal const int NonceSize = 12;
    internal const int CompressionHeaderSize = 1;
    internal const int MacSize = 128;
    internal const int BufferSize = 80 * 1024;
    internal const int HeaderSize = SaltSize + NonceSize + CompressionHeaderSize;
}
