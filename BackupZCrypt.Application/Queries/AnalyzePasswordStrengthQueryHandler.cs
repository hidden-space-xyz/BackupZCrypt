using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Password;

namespace BackupZCrypt.Application.Queries;

/// <summary>
/// Handles <see cref="AnalyzePasswordStrengthQuery"/> by delegating to the password service's pure
/// in-memory analysis.
/// </summary>
/// <param name="passwordService">The service that evaluates password strength.</param>
internal sealed class AnalyzePasswordStrengthQueryHandler(IPasswordService passwordService)
    : ISyncQueryHandler<AnalyzePasswordStrengthQuery, PasswordStrengthAnalysis>
{
    /// <summary>
    /// Analyzes the queried password's strength.
    /// </summary>
    /// <param name="query">The query carrying the password to analyze.</param>
    /// <returns>The analysis containing the strength rating, score, entropy, and localizable tips.</returns>
    public PasswordStrengthAnalysis Handle(AnalyzePasswordStrengthQuery query)
    {
        return passwordService.AnalyzePasswordStrength(query.Password);
    }
}
