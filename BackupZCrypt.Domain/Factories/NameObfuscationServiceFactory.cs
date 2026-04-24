namespace BackupZCrypt.Domain.Factories;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Resources;
using BackupZCrypt.Domain.Strategies.Interfaces;

internal sealed class NameObfuscationServiceFactory(IEnumerable<INameObfuscationStrategy> strategies)
    : INameObfuscationServiceFactory
{
    private readonly Dictionary<NameObfuscationMode, INameObfuscationStrategy> strategies =
        strategies.ToDictionary(s => s.Id, s => s);

    public INameObfuscationStrategy Create(NameObfuscationMode obfuscationMode)
    {
        return !this.strategies.TryGetValue(obfuscationMode, out var strategy)
            ? throw new ArgumentOutOfRangeException(
                nameof(obfuscationMode),
                string.Format(Messages.NameObfuscationModeNotRegisteredFormat, obfuscationMode))
            : strategy;
    }
}
