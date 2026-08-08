using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Application.Queries;

/// <summary>
/// Requests a cryptographically random password of the given length and character composition.
/// Answered synchronously because generation is a pure in-memory computation.
/// </summary>
/// <param name="Length">The number of characters the generated password must contain.</param>
/// <param name="Options">The flags selecting which character classes to include and exclusions to apply.</param>
public sealed record class GeneratePasswordQuery(
    int Length,
    PasswordGenerationOptions Options
) : IQuery<string>;
