namespace BackupZCrypt.Test;

/// <summary>
/// Selects the Microsoft Testing Platform host for test runs and preserves xUnit's console runner
/// for direct executable invocations.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Runs the test assembly with the host selected by xUnit's command-line contract.
    /// </summary>
    /// <param name="args">The command-line arguments passed to the test executable.</param>
    /// <returns>The test host's exit code.</returns>
    public static int Main(string[] args)
    {
        return args.Any(static arg => arg is "--server" or "--internal-msbuild-node")
            ? Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework
                .RunAsync(args, SelfRegisteredExtensions.AddSelfRegisteredExtensions)
                .GetAwaiter()
                .GetResult()
            : Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args).GetAwaiter().GetResult();
    }
}
