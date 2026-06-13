using BackupZCrypt.Infrastructure.Services;
using BackupZCrypt.Test.Common;

namespace BackupZCrypt.Test.Integration;

// FileOperationsService and SystemStorageService exercised against the real file system
// and the real temp drive.
public sealed class FileSystemServiceTests
{
    [Test]
    public async Task WriteThenReadBytes_Roundtrips()
    {
        using var dir = new TempDir();
        var service = new FileOperationsService();
        var path = dir.Combine("payload.bin");
        var content = new byte[] { 1, 2, 3, 4, 250, 251, 252, 0, 255 };

        await service.WriteAllBytesAsync(path, content);
        var read = await service.ReadAllBytesAsync(path);

        Assert.That(read, Is.EqualTo(content));
    }

    [Test]
    public async Task ComputeFileHashAsync_IsDeterministicAndContentSensitive()
    {
        using var dir = new TempDir();
        var service = new FileOperationsService();

        var fileA = dir.WriteText("a.txt", "identical content");
        var fileACopy = dir.WriteText("a-copy.txt", "identical content");
        var fileB = dir.WriteText("b.txt", "different content");

        var hashA = await service.ComputeFileHashAsync(fileA);
        var hashACopy = await service.ComputeFileHashAsync(fileACopy);
        var hashB = await service.ComputeFileHashAsync(fileB);

        // Same content -> same hash (also stable across two reads of the same file).
        Assert.That(hashACopy, Is.EqualTo(hashA));
        Assert.That(await service.ComputeFileHashAsync(fileA), Is.EqualTo(hashA));

        // Different content -> different hash.
        Assert.That(hashB, Is.Not.EqualTo(hashA));
    }

    [Test]
    public async Task GetFilesAsync_ReturnsAllFilesIncludingNested()
    {
        using var dir = new TempDir();
        var service = new FileOperationsService();

        var f1 = dir.WriteText("root.txt", "1");
        var f2 = dir.WriteText(Path.Combine("nested", "child.txt"), "2");
        var f3 = dir.WriteText(Path.Combine("nested", "deep", "grandchild.txt"), "3");

        var files = await service.GetFilesAsync(dir.Path);

        var asSet = files.ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.That(files.Length, Is.EqualTo(3));
        Assert.That(asSet.Contains(f1), Is.True);
        Assert.That(asSet.Contains(f2), Is.True);
        Assert.That(asSet.Contains(f3), Is.True);
    }

    [Test]
    public async Task CleanDirectoryAsync_EmptiesPopulatedDirectory()
    {
        using var dir = new TempDir();
        var service = new FileOperationsService();

        dir.WriteText("top.txt", "x");
        dir.WriteText(Path.Combine("sub", "inner.txt"), "y");

        await service.CleanDirectoryAsync(dir.Path);

        // The directory still exists but is now empty of any files or subdirectories.
        Assert.That(Directory.Exists(dir.Path), Is.True);
        Assert.That(Directory.GetFiles(dir.Path, "*", SearchOption.AllDirectories), Is.Empty);
        Assert.That(Directory.GetDirectories(dir.Path), Is.Empty);
    }

    [Test]
    public void GetFileSize_MatchesWrittenLength()
    {
        using var dir = new TempDir();
        var service = new FileOperationsService();

        var content = new byte[12345];
        var path = dir.WriteFile("sized.bin", content);

        Assert.That(service.GetFileSize(path), Is.EqualTo(content.Length));
    }

    [Test]
    public void SystemStorage_RealTempDrive_IsReadyAndReportsFreeSpace()
    {
        using var dir = new TempDir();
        var service = new SystemStorageService();

        var root = service.GetPathRoot(dir.Path);
        Assert.That(string.IsNullOrEmpty(root), Is.False, "Could not resolve the temp drive root.");

        Assert.That(service.IsDriveReady(root!), Is.True, $"Temp drive '{root}' reported not ready.");
        Assert.That(
            service.GetAvailableFreeSpace(root!),
            Is.GreaterThan(0),
            $"Temp drive '{root}' reported no free space."
        );
    }

    [Test]
    public void SystemStorage_InvalidRoot_IsNotReadyAndReportsNegativeSpace()
    {
        var service = new SystemStorageService();

        // A drive root that almost certainly does not exist on the test machine. Whether
        // DriveInfo treats it as not-ready or throws, the service must report unavailable.
        const string InvalidRoot = "Z:\\";

        Assert.That(service.IsDriveReady(InvalidRoot), Is.False);
        Assert.That(service.GetAvailableFreeSpace(InvalidRoot), Is.EqualTo(-1));
    }
}
