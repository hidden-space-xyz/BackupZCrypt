namespace CloudZCrypt.Domain.Factories;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Resources;
using CloudZCrypt.Domain.Strategies.Interfaces;

internal class CompressionServiceFactory(IEnumerable<ICompressionStrategy> strategies)
    : ICompressionServiceFactory
{
    private readonly IReadOnlyDictionary<CompressionMode, ICompressionStrategy> strategies =
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
