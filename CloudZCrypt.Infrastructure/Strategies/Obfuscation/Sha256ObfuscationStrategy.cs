namespace CloudZCrypt.Infrastructure.Strategies.Obfuscation;

using System.Security.Cryptography;
using System.Text;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Infrastructure.Resources;

internal class Sha256ObfuscationStrategy : INameObfuscationStrategy
{
    public NameObfuscationMode Id => NameObfuscationMode.Sha256;

    public string DisplayName => Messages.Sha256DisplayName;

    public string Description => Messages.Sha256Description;

    public string Summary => Messages.Sha256Summary;

    public string ObfuscateFileName(string sourceFilePath, string originalFileName)
    {
        string extension = Path.GetExtension(originalFileName);

        string hashName;
        if (File.Exists(sourceFilePath))
        {
            hashName = ComputeFileHash(sourceFilePath);
        }
        else
        {
            string basis = string.IsNullOrEmpty(sourceFilePath) ? originalFileName : sourceFilePath;
            hashName = ComputeStringHash(basis);
        }

        return hashName + extension;
    }

    private static string ComputeFileHash(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        byte[] hash = SHA256.HashData(stream);
        return ToHex(hash);
    }

    private static string ComputeStringHash(string input)
    {
        byte[] data = Encoding.UTF8.GetBytes(input);
        byte[] hash = SHA256.HashData(data);
        return ToHex(hash);
    }

    private static string ToHex(byte[] bytes)
    {
        StringBuilder sb = new(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString();
    }
}
