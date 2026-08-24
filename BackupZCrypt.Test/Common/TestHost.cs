using BackupZCrypt.Composition;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Common;

/// <summary>
/// Builds a service provider wired with the real domain and application services for integration tests.
/// </summary>
public static class TestHost
{
    /// <summary>
    /// Builds a provider containing production registrations, optionally followed by test overrides.
    /// </summary>
    /// <param name="configure">Optional registrations appended after the production services.</param>
    /// <returns>A new provider the caller owns and must dispose.</returns>
    public static ServiceProvider CreateProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection().AddBackupZCryptServices();

        configure?.Invoke(services);

        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
    }
}
