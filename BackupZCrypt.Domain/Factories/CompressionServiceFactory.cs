namespace BackupZCrypt.Domain.Factories;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.Resources;
using BackupZCrypt.Domain.Strategies.Interfaces;

internal sealed class CompressionServiceFactory(IEnumerable<ICompressionStrategy> strategies)
    : ICompressionServiceFactory
{
    private readonly Dictionary<CompressionMode, ICompressionStrategy> strategies =
        strategies.ToDictionary(s => s.Id, s => s);

    public ICompressionStrategy Create(CompressionMode mode)
    {
        return !this.strategies.TryGetValue(mode, out ICompressionStrategy? strategy)
            ? throw new ArgumentOutOfRangeException(
                nameof(mode),
                string.Format(Messages.CompressionModeNotRegisteredFormat, mode))
            : strategy;
    }
}
