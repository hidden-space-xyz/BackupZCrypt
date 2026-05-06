namespace BackupZCrypt.Application.Services.Interfaces;

public interface ISettingsService
{
    string GetFilePath<T>()
        where T : class, ISettings<T>;

    Task<T> GetOrCreateAsync<T>(CancellationToken cancellationToken = default)
        where T : class, ISettings<T>;

    Task SaveAsync<T>(T settings, CancellationToken cancellationToken = default)
        where T : class, ISettings<T>;
}
