using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;

namespace BackupZCrypt.Application.Queries;

/// <summary>
/// Handles <see cref="GeneratePasswordQuery"/> by delegating to the password service's
/// cryptographically random generator.
/// </summary>
/// <param name="passwordService">The service that generates random passwords.</param>
internal sealed class GeneratePasswordQueryHandler(IPasswordService passwordService)
    : ISyncQueryHandler<GeneratePasswordQuery, string>
{
    /// <summary>
    /// Generates the requested password.
    /// </summary>
    /// <param name="query">The query carrying the length and character options.</param>
    /// <returns>A randomly generated password.</returns>
    public string Handle(GeneratePasswordQuery query)
    {
        return passwordService.GeneratePassword(query.Length, query.Options);
    }
}
