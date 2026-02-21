namespace BackupZCrypt.Infrastructure.Strategies.Obfuscation;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Resources;

internal class NoObfuscationStrategy : INameObfuscationStrategy
{
    public NameObfuscationMode Id => NameObfuscationMode.None;

    public string DisplayName => Messages.NoObfuscationDisplayName;

    public string Description => Messages.NoObfuscationDescription;

    public string Summary => Messages.NoObfuscationSummary;

    public string ObfuscateFileName(string sourceFilePath, string originalFileName)
    {
        return originalFileName;
    }
}
