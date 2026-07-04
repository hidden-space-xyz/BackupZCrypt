using BackupZCrypt.Composition;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Common;

/// <summary>
/// Builds a service provider wired with the real domain and application services for integration tests.
/// </summary>
public static class TestHost
{
    public static ServiceProvider CreateProvider()
    {
        return new ServiceCollection()
            .AddDomainServices()
            .AddApplicationServices()
            .BuildServiceProvider();
    }
}
