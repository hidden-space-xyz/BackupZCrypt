using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace BackupZCrypt.Test;

[SetUpFixture]
internal sealed class GlobalSetUp
{
    [SuppressMessage(
        "Structure",
        "NUnit1028:Only test methods should be public",
        Justification = "NUnit requires public setup methods for this fixture.")]
    [OneTimeSetUp]
    public void RunBeforeAnyTests()
    {
        SetInvariantCulture();
    }

    [SuppressMessage(
        "Structure",
        "NUnit1028:Only test methods should be public",
        Justification = "Module initializer is infrastructure code, not an NUnit test method.")]
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
