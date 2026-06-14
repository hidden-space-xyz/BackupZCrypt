using BackupZCrypt.Composition;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Common;

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
