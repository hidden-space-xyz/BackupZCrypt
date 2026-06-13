using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Test.Unit.Application;

public sealed class PathNormalizationHelperTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryNormalize_EmptyOrWhitespace_ReturnsEmptyWithoutError(string rawPath)
    {
        var result = PathNormalizationHelper.TryNormalize(rawPath, out var error);

        Assert.Equal(string.Empty, result);
        Assert.Null(error);
    }

    [Fact]
    public void TryNormalize_RelativePath_ReturnsRootedFullPathWithoutError()
    {
        var result = PathNormalizationHelper.TryNormalize("some-relative-folder", out var error);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.True(Path.IsPathRooted(result));
    }

    [Fact]
    public void TryNormalize_InvalidPath_ReturnsNullAndInvalidPathFormatError()
    {
        // A path far beyond the OS limit makes normalization throw; the helper must
        // catch it and surface InvalidPathFormat. (A NUL char can't be used here: on
        // Windows ExpandEnvironmentVariables truncates the string at the first NUL
        // before normalization ever sees it.)
        var tooLong = new string('a', 300_000);

        var result = PathNormalizationHelper.TryNormalize(tooLong, out var error);

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Equal(MessageCode.InvalidPathFormat, error!.Code);
    }
}
