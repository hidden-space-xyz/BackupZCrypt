using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.Services.Interfaces;

namespace BackupZCrypt.Application.Commands;

/// <summary>
/// Requests that the given settings of type <typeparamref name="TSettings"/> be persisted.
/// </summary>
/// <remarks>
/// One generic message serves every settings type; the composition root closes it once per supported
/// type, which keeps the supported set an explicit list without a message pair per type.
/// </remarks>
/// <typeparam name="TSettings">The settings type to save.</typeparam>
/// <param name="Settings">The settings instance to persist.</param>
public sealed record class SaveSettingsCommand<TSettings>(TSettings Settings) : ICommand<Result>
    where TSettings : class, ISettings<TSettings>;
