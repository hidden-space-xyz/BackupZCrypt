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
    [Fact]
    internal void GetAvailableFreeSpace_EmptyRoot_ReturnsMinusOneWithoutThrowing()
    {
        var service = new SystemStorageService();

        Assert.Equal(-1L, service.GetAvailableFreeSpace(string.Empty));
    }

    [Fact]
    internal void IsDriveReady_EmptyRoot_ReturnsFalseWithoutThrowing()
    {
        var service = new SystemStorageService();

        Assert.False(service.IsDriveReady(string.Empty));
    }

    [Fact]
    internal void GetPathRoot_AndTheDriveQueries_AgreeOnTheVolumeHoldingTheTempDirectory()
    {
        var service = new SystemStorageService();
        var probe = Path.GetTempPath();

        var root = service.GetPathRoot(probe);

        Assert.Multiple(
            () => Assert.NotNull(root),
            () => Assert.NotEmpty(root!),
            () => Assert.StartsWith(root!, probe, StringComparison.Ordinal),
            () =>
                Assert.True(
                    service.IsDriveReady(root!),
                    "The volume holding the temp directory was reported as not ready."
                ),
            () =>
                Assert.True(
                    service.GetAvailableFreeSpace(root!) > 0,
                    "The volume holding the temp directory reported no free space, so the validator "
                        + "would refuse every backup."
                )
        );
    }

    [Fact]
    internal void GetPathRoot_PathWithNoVolume_ReturnsTheUnknownSentinelWithoutThrowing()
    {
        var service = new SystemStorageService();

        Assert.Multiple(
            () => Assert.Empty(service.GetPathRoot(Path.Combine("relative", "destination"))!),
            () => Assert.Null(service.GetPathRoot(string.Empty))
        );
    }
}
