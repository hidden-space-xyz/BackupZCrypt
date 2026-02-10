using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Factories.Interfaces;
using CloudZCrypt.Domain.Strategies.Interfaces;

namespace CloudZCrypt.Domain.Factories;

internal class CompressionServiceFactory(IEnumerable<ICompressionStrategy> strategies)
    : ICompressionServiceFactory
{
    private readonly IReadOnlyDictionary<CompressionMode, ICompressionStrategy> strategies =
        strategies.ToDictionary(s => s.Id, s => s);

    public ICompressionStrategy Create(CompressionMode mode)
    {
        return !strategies.TryGetValue(mode, out ICompressionStrategy? strategy)
            ? throw new ArgumentOutOfRangeException(
                nameof(mode),
                $"Compression mode '{mode}' is not registered."
            )
            : strategy;
    }
}
