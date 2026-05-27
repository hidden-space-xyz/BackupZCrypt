namespace BackupZCrypt.Domain.Constants;

public static class EncryptionConstants
{
    public const int KeySize = 256;
    public const int SaltSize = 32;
    public const int NonceSize = 12;
    public const int MacSize = 128;
    public const int TagSize = MacSize / 8;
}
