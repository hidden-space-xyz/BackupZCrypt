using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Infrastructure.Resources;

namespace CloudZCrypt.Infrastructure.Strategies.Obfuscation;

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
