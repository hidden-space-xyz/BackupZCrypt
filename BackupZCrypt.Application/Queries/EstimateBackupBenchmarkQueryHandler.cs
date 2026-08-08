using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Benchmark;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Queries;

/// <summary>
/// Handles <see cref="EstimateBackupBenchmarkQuery"/> by running the benchmark service and mapping
/// its failures onto the result contract instead of letting them escape as exceptions.
/// </summary>
/// <param name="benchmarkService">The service that measures the selected algorithms.</param>
internal sealed class EstimateBackupBenchmarkQueryHandler(IBackupBenchmarkService benchmarkService)
    : IQueryHandler<EstimateBackupBenchmarkQuery, Result<BenchmarkEstimate>>
{
    /// <summary>
    /// Runs the requested benchmark.
    /// </summary>
    /// <param name="query">The query carrying the algorithms and data size to estimate for.</param>
    /// <param name="cancellationToken">A token to cancel the benchmark.</param>
    /// <returns>The estimate, or a failure describing why the benchmark could not run.</returns>
    public async Task<Result<BenchmarkEstimate>> HandleAsync(
        EstimateBackupBenchmarkQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var request = new BenchmarkRequest(
            query.EncryptionAlgorithm,
            query.KeyDerivationAlgorithm,
            query.Compression,
            query.DataBytes
        );

        try
        {
            return await benchmarkService.EstimateAsync(request, cancellationToken);
        }
        catch (Exception exception)
            when (exception is not OutOfMemoryException and not OperationCanceledException)
        {
            return Result<BenchmarkEstimate>.Failure(
                MessageCode.UnexpectedErrorFormat,
                exception.Message
            );
        }
    }
}
