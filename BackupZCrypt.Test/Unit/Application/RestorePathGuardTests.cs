using BackupZCrypt.Application.Engine;
using BackupZCrypt.Infrastructure.Services;
using BackupZCrypt.Test.Common;
using BackupZCrypt.Test.Unit.Domain;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for <see cref="RestorePathGuard"/>, the host-side check that decides where on the
/// running system a restore is allowed to write an entry the manifest path rules accepted.
/// </summary>
/// <remarks>
/// This is the check that keeps a crafted manifest from steering a restore outside the destination
/// directory, directly or through a link that already exists below it.
/// </remarks>
public sealed class RestorePathGuardTests
{
    [Theory]
    [MemberData(
        nameof(ManifestPathPolicyTests.WindowsIncompatiblePaths),
        MemberType = typeof(ManifestPathPolicyTests)
    )]
    internal void ResolveSafeDestination_OnWindows_RefusesNamesItCannotCreate(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Only Windows refuses these names.");
        }

        _ = Assert.Throws<InvalidDataException>(
            () => RestorePathGuard.ResolveSafeDestination(Path.GetTempPath(), path)
        );
    }

    [Fact]
    internal void ResolveSafeDestination_RootedPath_IsRejected()
    {
        var rooted = Path.Join(Path.GetTempPath(), "escape.txt");

        _ = Assert.Throws<InvalidDataException>(
            () => RestorePathGuard.ResolveSafeDestination(Path.Join(Path.GetTempPath(), "root"), rooted)
        );
    }

    [Fact]
    internal void ResolveSafeDestination_OrdinaryEntry_LandsInsideTheRoot()
    {
        var root = Path.Join(Path.GetTempPath(), "bzc-policy-root");

        var resolved = RestorePathGuard.ResolveSafeDestination(root, "docs/sub/deep.txt");

        Assert.StartsWith(
            Path.GetFullPath(root) + Path.DirectorySeparatorChar,
            resolved,
            StringComparison.Ordinal
        );
    }

    [Fact]
    internal void ResolveSafeDestination_SiblingWhoseNameSharesThePrefix_IsNotTreatedAsInside()
    {
        var root = Path.Join(Path.GetTempPath(), "bzc-root");

        _ = Assert.Throws<InvalidDataException>(
            () => RestorePathGuard.ResolveSafeDestination(root, "../bzc-root-evil/escape.txt")
        );
    }

    [Fact]
    internal void EnsureNoReparsePointDescendants_LinkBelowRoot_IsRejected()
    {
        using var dir = new TempDir();
        var service = new FileOperationsService();
        var root = dir.Combine("root");
        var outside = dir.Combine("outside");
        _ = Directory.CreateDirectory(root);
        _ = Directory.CreateDirectory(outside);
        var link = Path.Join(root, "linked");

        try
        {
            _ = Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip("This platform refuses directory symbolic links: " + ex.Message);
        }

        _ = Assert.Throws<InvalidDataException>(
            () =>
                RestorePathGuard.EnsureNoReparsePointDescendants(
                    service,
                    root,
                    Path.Join(link, "nested")
                )
        );
    }
}
