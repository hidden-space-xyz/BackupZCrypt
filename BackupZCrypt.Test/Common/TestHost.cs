using BackupZCrypt.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Common;

// Builds the same backup pipeline the desktop app uses (everything below the UI),
// so integration tests exercise the real wired graph rather than hand-assembled
// fakes.
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
