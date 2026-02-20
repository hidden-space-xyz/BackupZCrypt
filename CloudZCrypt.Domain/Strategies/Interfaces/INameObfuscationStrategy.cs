namespace CloudZCrypt.Domain.Strategies.Interfaces;

using CloudZCrypt.Domain.Enums;

public interface INameObfuscationStrategy
{
    NameObfuscationMode Id { get; }

    string DisplayName { get; }

    string Description { get; }

    string Summary { get; }

    string ObfuscateFileName(string sourceFilePath, string originalFileName);
}
