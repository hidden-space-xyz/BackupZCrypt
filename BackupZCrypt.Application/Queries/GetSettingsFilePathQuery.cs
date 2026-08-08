using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;

namespace BackupZCrypt.Application.Queries;

/// <summary>
/// Requests the absolute path of the file backing the settings type
/// <typeparamref name="TSettings"/>, so the UI can show the user where their preferences live.
/// Answered synchronously because path resolution is a pure in-memory computation.
/// </summary>
/// <typeparam name="TSettings">The settings type whose file path is requested.</typeparam>
public sealed record class GetSettingsFilePathQuery<TSettings> : IQuery<string>
    where TSettings : class, ISettings<TSettings>;
