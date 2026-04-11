using System.Globalization;
using System.Runtime.CompilerServices;

namespace BackupZCrypt.Test;

[SetUpFixture]
internal sealed class GlobalSetUp
{
    [OneTimeSetUp]
    public void RunBeforeAnyTests()
    {
        SetInvariantCulture();
    }

    [ModuleInitializer]
    internal static void Initialize()
    {
        SetInvariantCulture();
    }

    private static void SetInvariantCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}
