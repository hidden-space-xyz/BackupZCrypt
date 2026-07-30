using BackupZCrypt.Composition;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Common;

/// <summary>
/// Builds a service provider wired with the real domain and application services for integration tests.
/// </summary>
public static class TestHost
{
    /// <summary>
    /// Builds a provider containing the production domain and application registrations, so tests
    /// exercise the real crypto, chunking, and orchestration stack rather than substitutes.
    /// </summary>
    /// <returns>A new provider the caller owns and must dispose.</returns>
    public static ServiceProvider CreateProvider()
    {
        return new ServiceCollection()
            .AddDomainServices()
            .AddApplicationServices()
            .BuildServiceProvider();
    }
}
