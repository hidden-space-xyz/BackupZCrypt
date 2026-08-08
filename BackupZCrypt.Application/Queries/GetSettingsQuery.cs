using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Domain.Services.Interfaces;

namespace BackupZCrypt.Application.Queries;

/// <summary>
/// Requests the persisted settings of type <typeparamref name="TSettings"/>, falling back to the
/// type's defaults when nothing valid is stored.
/// </summary>
/// <remarks>
/// One generic message serves every settings type; the composition root closes it once per supported
/// type, which keeps the supported set an explicit list without a message pair per type.
/// </remarks>
/// <typeparam name="TSettings">The settings type to load.</typeparam>
public sealed record class GetSettingsQuery<TSettings> : IQuery<TSettings>
    where TSettings : class, ISettings<TSettings>;
