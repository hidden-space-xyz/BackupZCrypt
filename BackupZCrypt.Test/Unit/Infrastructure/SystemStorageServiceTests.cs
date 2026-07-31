using BackupZCrypt.Infrastructure.Services;

namespace BackupZCrypt.Test.Unit.Infrastructure;

/// <summary>
/// Unit tests for <see cref="SystemStorageService"/>'s never-throw contract. The validator treats
/// the sentinels (<c>-1</c> free space, <see langword="false"/> readiness) as "unknown, skip the
/// check" rather than "block the backup", so an exception escaping this service would turn an
/// unreadable drive into a crash instead of a skipped precondition. The reverse also matters, so
/// the volume actually holding the test's temp directory is checked to report itself as ready with
/// real free space rather than as permanently unknown.
/// </summary>
/// <remarks>
/// <para>
/// The empty root is the realistic degenerate input rather than a contrived one: <c>GetPathRoot</c>
/// returns an empty string for any relative destination path, and only the validator's
/// <c>IsNullOrEmpty</c> guard keeps it from reaching <c>DriveInfo</c> today. A service that answered
/// "unknown" for every path would satisfy the sentinel cases while silently disabling the free-space
/// precondition, and a backup would then run until the destination filled instead of being refused up
/// front.
/// </para>
/// <para>
/// Every case is portable by construction: on Windows the <c>DriveInfo</c> constructor rejects an
/// empty drive name and elsewhere the volume is simply never ready, while the temp directory is
/// rooted on a mounted volume on Windows (<c>C:\</c>) and Linux (<c>/</c>) alike.
/// </para>
/// </remarks>
public sealed class SystemStorageServiceTests
{
    [Test]
    public void GetAvailableFreeSpace_EmptyRoot_ReturnsMinusOneWithoutThrowing()
    {
        var service = new SystemStorageService();

        Assert.That(service.GetAvailableFreeSpace(string.Empty), Is.EqualTo(-1));
    }

    [Test]
    public void IsDriveReady_EmptyRoot_ReturnsFalseWithoutThrowing()
    {
        var service = new SystemStorageService();

        Assert.That(service.IsDriveReady(string.Empty), Is.False);
    }

    [Test]
    public void GetPathRoot_AndTheDriveQueries_AgreeOnTheVolumeHoldingTheTempDirectory()
    {
        var service = new SystemStorageService();
        var probe = Path.GetTempPath();

        var root = service.GetPathRoot(probe);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root, Is.Not.Null.And.Not.Empty);
            Assert.That(
                probe,
                Does.StartWith(root!),
                "The reported root is not a prefix of the path it was derived from."
            );
            Assert.That(
                service.IsDriveReady(root!),
                Is.True,
                "The volume holding the temp directory was reported as not ready."
            );
            Assert.That(
                service.GetAvailableFreeSpace(root!),
                Is.GreaterThan(0),
                "The volume holding the temp directory reported no free space, so the validator "
                    + "would refuse every backup."
            );
        }
    }

    [Test]
    public void GetPathRoot_PathWithNoVolume_ReturnsTheUnknownSentinelWithoutThrowing()
    {
        var service = new SystemStorageService();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                service.GetPathRoot(Path.Combine("relative", "destination")),
                Is.Empty,
                "An unrooted destination must yield an empty root. BackupRequestValidator gates "
                    + "both drive checks on string.IsNullOrEmpty(root), and anything else - the "
                    + "path echoed back, say - would be handed straight to DriveInfo as a drive "
                    + "name."
            );
            Assert.That(
                service.GetPathRoot(string.Empty),
                Is.Null,
                "An empty destination must yield a null root, the other shape of \"no volume\" the "
                    + "validator gates on."
            );
        }
    }
}
