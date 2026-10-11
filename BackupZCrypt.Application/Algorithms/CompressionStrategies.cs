using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Application.Algorithms;

/// <summary>
/// Looks up the compression an archive records, treating <see cref="CompressionMode.None"/> as no
/// strategy at all.
/// </summary>
internal static class CompressionStrategies
{
    /// <summary>
    /// Resolves the compression strategy for a mode, treating <see cref="CompressionMode.None"/> as
    /// no strategy so chunks bypass the compression path entirely.
    /// </summary>
    /// <param name="strategies">The registered compression strategies.</param>
    /// <param name="compressionMode">The compression mode recorded for the backup.</param>
    /// <returns>The compression strategy, or <see langword="null"/> when compression is disabled.</returns>
    internal static ICompressionStrategy? ResolveOrNull(
        this IStrategyRegistry<CompressionMode, ICompressionStrategy> strategies,
        CompressionMode compressionMode
    )
    {
        return compressionMode is CompressionMode.None ? null : strategies.Resolve(compressionMode);
    }
}
