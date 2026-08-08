using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Manifest;

namespace BackupZCrypt.Application.Queries;

/// <summary>
/// Handles <see cref="DetectManifestKindQuery"/> by probing the path through the manifest service,
/// absorbing probe failures into <see cref="ManifestKind.Missing"/>.
/// </summary>
/// <remarks>
/// <see cref="ManifestKind.Missing"/> already means "no readable manifest was found there", so a
/// probe that throws — an unreadable directory, a permission error — is reported as exactly that
/// rather than surfacing an exception the caller would map to the same answer anyway.
/// </remarks>
/// <param name="manifestService">The service that inspects manifests on disk.</param>
internal sealed class DetectManifestKindQueryHandler(IManifestService manifestService)
    : IQueryHandler<DetectManifestKindQuery, ManifestKind>
{
    /// <summary>
    /// Detects the manifest kind at the queried path.
    /// </summary>
    /// <param name="query">The query carrying the path to probe.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The detected kind, or <see cref="ManifestKind.Missing"/> when the probe fails.</returns>
    public async Task<ManifestKind> HandleAsync(
        DetectManifestKindQuery query,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await manifestService.DetectManifestKindAsync(query.BackupPath, cancellationToken);
        }
        catch (Exception exception)
            when (exception is not OutOfMemoryException and not OperationCanceledException)
        {
            return ManifestKind.Missing;
        }
    }
}
