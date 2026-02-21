namespace BackupZCrypt.Infrastructure.Strategies.Obfuscation;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Resources;

internal class GuidObfuscationStrategy : INameObfuscationStrategy
{
    public NameObfuscationMode Id => NameObfuscationMode.Guid;

    public string DisplayName => Messages.GuidDisplayName;

    public string Description => Messages.GuidDescription;

    public string Summary => Messages.GuidSummary;

    public string ObfuscateFileName(string sourceFilePath, string originalFileName)
    {
        string extension = Path.GetExtension(originalFileName);
        string guidName = Guid.NewGuid().ToString();
        return guidName + extension;
    }
}
