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

    [Test]
    public async Task ComputeFileHashAsync_MatchesKnownVectorsAndIsContentSensitive()
    {
        using var dir = new TempDir();
        var service = new FileOperationsService();

        var abc = dir.WriteText("abc.txt", "abc");
        var abcCopy = dir.WriteText("abc-copy.txt", "abc");
        var empty = dir.WriteFile("empty.bin", []);
        var other = dir.WriteText("other.txt", "different content");

        var hashAbc = await service.ComputeFileHashAsync(abc);
        var hashAbcCopy = await service.ComputeFileHashAsync(abcCopy);
        var hashEmpty = await service.ComputeFileHashAsync(empty);
        var hashOther = await service.ComputeFileHashAsync(other);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hashAbc, Is.EqualTo(Sha256OfAbc), "No longer Base64-encoded SHA-256.");
            Assert.That(hashEmpty, Is.EqualTo(Sha256OfEmpty));
            Assert.That(hashAbcCopy, Is.EqualTo(hashAbc));
            Assert.That(hashOther, Is.Not.EqualTo(hashAbc));
        }
    }

    [Test]
    public async Task GetFilesAsync_ReturnsEveryFileIncludingNested()
    {
        using var dir = new TempDir();
        var service = new FileOperationsService();

        var root = dir.WriteText("root.txt", "1");
        var child = dir.WriteText(Path.Combine("nested", "child.txt"), "2");
        var grandchild = dir.WriteText(Path.Combine("nested", "deep", "grandchild.txt"), "3");

        var files = await service.GetFilesAsync(dir.Path);

        Assert.That(
            files,
            Is.EquivalentTo([root, child, grandchild]),
            "Enumeration must return the written paths verbatim. The comparison is deliberately ordinal: the "
                + "manifest's relative paths are computed from these strings, so on the case-sensitive file "
                + "systems CI runs on a difference in casing is a real defect rather than noise."
        );
    }

    [Test]
    public async Task GetFilesAsync_WithSearchPattern_ReturnsOnlyMatchingFiles()
    {
        using var dir = new TempDir();
        var service = new FileOperationsService();

        var rootText = dir.WriteText("a.txt", "1");
        var nestedText = dir.WriteText(Path.Combine("sub", "c.txt"), "3");
        _ = dir.WriteText("b.log", "2");
        _ = dir.WriteText(Path.Combine("sub", "d.log"), "4");

        var files = await service.GetFilesAsync(dir.Path, "*.txt");

        Assert.That(files, Is.EquivalentTo([rootText, nestedText]));
    }

    [Test]
    public async Task GetFilesAsync_DirectorySymbolicLink_IsNotTraversed()
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
            Assert.Ignore("This platform refuses directory symbolic links: " + ex.Message);
        }

        var files = await service.GetFilesAsync(source);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(files, Does.Contain(included));
            Assert.That(
                files.Where(path => path.EndsWith("secret.txt", StringComparison.Ordinal)),
                Is.Empty,
                "Enumeration followed a directory symbolic link out of the source tree. The promise that "
                    + "traversal cannot cycle or descend outside the source rests entirely on the recursion "
                    + "predicate skipping reparse points, and nothing else asserts it: without it a junction "
                    + "loop hangs the backup, or files outside the source are silently archived."
            );
        }
    }

    [Test]
    public async Task CleanDirectoryAsync_EmptiesPopulatedDirectory()
    {
        using var dir = new TempDir();
        var service = new FileOperationsService();

        _ = dir.WriteText("top.txt", "x");
        _ = dir.WriteText(Path.Combine("sub", "inner.txt"), "y");

        await service.CleanDirectoryAsync(dir.Path);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(dir.Path), Is.True);
            Assert.That(Directory.GetFiles(dir.Path, "*", SearchOption.AllDirectories), Is.Empty);
            Assert.That(Directory.GetDirectories(dir.Path), Is.Empty);
        }
    }

    [Test]
    public void SystemStorage_RealTempDrive_IsReadyAndReportsFreeSpace()
    {
        using var dir = new TempDir();
        var service = new SystemStorageService();

        var root = service.GetPathRoot(dir.Path);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(string.IsNullOrEmpty(root), Is.False, "Could not resolve the temp drive root.");

            Assert.That(service.IsDriveReady(root!), Is.True, $"Temp drive '{root}' reported not ready.");
            Assert.That(
                service.GetAvailableFreeSpace(root!),
                Is.GreaterThan(0),
                $"Temp drive '{root}' reported no free space."
            );
        }
    }

    [Test]
    public void SystemStorage_UnmountedRoot_IsNotReadyAndReportsNegativeSpace()
    {
        var service = new SystemStorageService();
        var unmountedRoot = FindUnmountedRoot();

        if (unmountedRoot is null)
        {
            Assert.Ignore("Every drive letter is assigned, so no unmounted root is available.");
        }
        else
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    service.IsDriveReady(unmountedRoot),
                    Is.False,
                    $"'{unmountedRoot}' reported ready."
                );
                Assert.That(
                    service.GetAvailableFreeSpace(unmountedRoot),
                    Is.EqualTo(-1),
                    "An unreachable root must report the -1 sentinel the validator turns into a warning the "
                        + "user can act on, instead of throwing and crashing the app on an unreachable "
                        + "destination."
                );
            }
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
