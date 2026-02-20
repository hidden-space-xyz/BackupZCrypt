namespace CloudZCrypt.Infrastructure.Strategies.Obfuscation;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Infrastructure.Resources;

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
