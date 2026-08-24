using BackupZCrypt.Infrastructure.Services;
using BackupZCrypt.Test.Common;

namespace BackupZCrypt.Test.Integration;

/// <summary>
/// Integration tests exercising the file operations and system storage services against a real
/// temporary directory and the drive that hosts it.
/// </summary>
public sealed class FileSystemServiceTests
{
    /// <summary>
    /// The Base64 SHA-256 digest of the three ASCII bytes <c>abc</c>, the standard published vector.
    /// </summary>
    /// <remarks>
    /// The published vectors are pinned rather than merely compared for determinism: restore and verify
    /// compare every reconstructed file against this value, and every manifest ever written holds it.
    /// Determinism alone would survive a switch to SHA-1, CRC32, or from Base64 to hex, and such a silent
    /// digest change is unrecoverable for the archives already on disk.
    /// </remarks>
    private const string Sha256OfAbc = "ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=";

    /// <summary>
    /// The Base64 SHA-256 digest of an empty input, which also covers the zero-length stream path.
    /// </summary>
    private const string Sha256OfEmpty = "47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=";

    [Fact]
    internal async Task ComputeFileHashAsync_MatchesKnownVectorsAndIsContentSensitive()
    {
        using var dir = new TempDir();
        var service = new FileOperationsService();

        var abc = dir.WriteText("abc.txt", "abc");
        var abcCopy = dir.WriteText("abc-copy.txt", "abc");
        var empty = dir.WriteFile("empty.bin", []);
        var other = dir.WriteText("other.txt", "different content");

        var hashAbc = await service.ComputeFileHashAsync(
            abc,
            TestContext.Current.CancellationToken
        );
        var hashAbcCopy = await service.ComputeFileHashAsync(
            abcCopy,
            TestContext.Current.CancellationToken
        );
        var hashEmpty = await service.ComputeFileHashAsync(
            empty,
            TestContext.Current.CancellationToken
        );
        var hashOther = await service.ComputeFileHashAsync(
            other,
            TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.Equal(Sha256OfAbc, hashAbc),
            () => Assert.Equal(Sha256OfEmpty, hashEmpty),
            () => Assert.Equal(hashAbc, hashAbcCopy),
            () => Assert.NotEqual(hashAbc, hashOther)
        );
    }

    [Fact]
    internal async Task GetFilesAsync_ReturnsEveryFileIncludingNested()
    {
        using var dir = new TempDir();
        var service = new FileOperationsService();

        var root = dir.WriteText("root.txt", "1");
        var child = dir.WriteText(Path.Combine("nested", "child.txt"), "2");
        var grandchild = dir.WriteText(Path.Combine("nested", "deep", "grandchild.txt"), "3");

        var files = await service.GetFilesAsync(
            dir.Path,
            cancellationToken: TestContext.Current.CancellationToken
        );

        string[] expected = [root, child, grandchild];
        Assert.Equivalent(expected, files, strict: true);
    }

    [Fact]
    internal async Task GetFilesAsync_WithSearchPattern_ReturnsOnlyMatchingFiles()
    {
        using var dir = new TempDir();
        var service = new FileOperationsService();

        var rootText = dir.WriteText("a.txt", "1");
        var nestedText = dir.WriteText(Path.Combine("sub", "c.txt"), "3");
        _ = dir.WriteText("b.log", "2");
        _ = dir.WriteText(Path.Combine("sub", "d.log"), "4");

        var files = await service.GetFilesAsync(
            dir.Path,
            "*.txt",
            TestContext.Current.CancellationToken
        );

        string[] expected = [rootText, nestedText];
        Assert.Equivalent(expected, files, strict: true);
    }

    [Fact]
    internal async Task GetFilesAsync_DirectorySymbolicLink_IsNotTraversed()
    {
        using var dir = new TempDir();
        var service = new FileOperationsService();

        var source = dir.Combine("source");
        _ = Directory.CreateDirectory(source);
        var included = dir.WriteText(Path.Combine("source", "root.txt"), "1");
        _ = dir.WriteText(Path.Combine("outside", "secret.txt"), "2");

        try
        {
            _ = Directory.CreateSymbolicLink(Path.Combine(source, "link"), dir.Combine("outside"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("This platform refuses directory symbolic links: " + ex.Message);
        }

        var files = await service.GetFilesAsync(
            source,
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.Contains(included, files),
            () =>
                Assert.DoesNotContain(
                    files,
                    path => path.EndsWith("secret.txt", StringComparison.Ordinal)
                )
        );
    }

    [Fact]
    internal async Task GetFilesAsync_FileSymbolicLink_IsExcluded()
    {
        using var dir = new TempDir();
        var service = new FileOperationsService();

        var source = dir.Combine("source");
        _ = Directory.CreateDirectory(source);
        var included = dir.WriteText(Path.Combine("source", "root.txt"), "1");
        var outside = dir.WriteText(Path.Combine("outside", "secret.txt"), "2");
        var link = Path.Combine(source, "linked-secret.txt");

        try
        {
            _ = File.CreateSymbolicLink(link, outside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("This platform refuses file symbolic links: " + ex.Message);
        }

        var files = await service.GetFilesAsync(
            source,
            cancellationToken: TestContext.Current.CancellationToken
        );

        Assert.Multiple(
            () => Assert.Contains(included, files),
            () => Assert.DoesNotContain(link, files)
        );
    }

    [Fact]
    internal async Task WriteFileAtomicallyAsync_WriterFails_PreservesTargetAndDeletesTemporaryFile()
    {
        using var dir = new TempDir();
        var service = new FileOperationsService();
        var target = dir.WriteText("settings.json", "known-good");

        var exception = await Assert.ThrowsAsync<IOException>(
            () =>
                service.WriteFileAtomicallyAsync(
                    target,
                    async (stream, token) =>
                    {
                        await stream.WriteAsync("partial"u8.ToArray(), token);
                        throw new IOException("injected writer failure");
                    },
                    TestContext.Current.CancellationToken
                )
        );

        Assert.Multiple(
            () => Assert.Equal("injected writer failure", exception.Message),
            () => Assert.Equal("known-good", File.ReadAllText(target)),
            () =>
                Assert.Empty(
                    Directory.GetFiles(dir.Path, "*.tmp", SearchOption.TopDirectoryOnly)
                )
        );
    }

    [Fact]
    internal async Task CleanDirectoryAsync_EmptiesPopulatedDirectory()
    {
        using var dir = new TempDir();
        var service = new FileOperationsService();

        _ = dir.WriteText("top.txt", "x");
        _ = dir.WriteText(Path.Combine("sub", "inner.txt"), "y");

        await service.CleanDirectoryAsync(dir.Path, TestContext.Current.CancellationToken);

        Assert.Multiple(
            () => Assert.True(Directory.Exists(dir.Path)),
            () => Assert.Empty(Directory.GetFiles(dir.Path, "*", SearchOption.AllDirectories)),
            () => Assert.Empty(Directory.GetDirectories(dir.Path))
        );
    }

    [Fact]
    internal void SystemStorage_RealTempDrive_IsReadyAndReportsFreeSpace()
    {
        using var dir = new TempDir();
        var service = new SystemStorageService();

        var root = service.GetPathRoot(dir.Path);
        Assert.Multiple(
            () => Assert.False(string.IsNullOrEmpty(root), "Could not resolve the temp drive root."),
            () => Assert.True(service.IsDriveReady(root!), $"Temp drive '{root}' reported not ready."),
            () =>
                Assert.True(
                    service.GetAvailableFreeSpace(root!) > 0,
                    $"Temp drive '{root}' reported no free space."
                )
        );
    }

    [Fact]
    internal void SystemStorage_UnmountedRoot_IsNotReadyAndReportsNegativeSpace()
    {
        var service = new SystemStorageService();
        var unmountedRoot = FindUnmountedRoot();

        if (unmountedRoot is null)
        {
            Assert.Skip("Every drive letter is assigned, so no unmounted root is available.");
        }
        else
        {
            Assert.Multiple(
                () => Assert.False(service.IsDriveReady(unmountedRoot), $"'{unmountedRoot}' reported ready."),
                () => Assert.Equal(-1L, service.GetAvailableFreeSpace(unmountedRoot))
            );
        }
    }

    /// <summary>
    /// Builds an absolute root that cannot resolve to a mounted volume: the highest unassigned drive
    /// letter on Windows, or a uniquely named absolute directory that cannot exist elsewhere.
    /// </summary>
    /// <remarks>
    /// A bare <c>Z:\</c> literal is meaningless off Windows — Unix accepts a backslash in a file name, so
    /// it resolves against the working directory and the test asserts nothing about unmounted volumes.
    /// Both branches here return an absolute path, so the outcome never depends on the working directory.
    /// </remarks>
    /// <returns>The unmounted root, or <see langword="null"/> when Windows has no free drive letter.</returns>
    private static string? FindUnmountedRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Path.GetPathRoot(Path.GetTempPath()) ?? Path.DirectorySeparatorChar.ToString(),
                "bzc-unmounted-" + Guid.NewGuid().ToString("N")
            );
        }

        var assigned = DriveInfo
            .GetDrives()
            .Select(drive => char.ToUpperInvariant(drive.Name[0]))
            .ToHashSet();

        for (var letter = 'Z'; letter >= 'D'; letter--)
        {
            if (!assigned.Contains(letter))
            {
                return $"{letter}:{Path.DirectorySeparatorChar}";
            }
        }

        return null;
    }
}
