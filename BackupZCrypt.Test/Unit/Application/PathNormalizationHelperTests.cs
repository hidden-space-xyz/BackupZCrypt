using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the path normalization helper.
/// </summary>
public sealed class PathNormalizationHelperTests
{
    [TestCase("")]
    [TestCase("   ")]
    public void TryNormalize_EmptyOrWhitespace_ReturnsEmptyWithoutError(string rawPath)
    {
        var result = PathNormalizationHelper.TryNormalize(rawPath, out var error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.EqualTo(string.Empty));
            Assert.That(error, Is.Null);
        }
    }

    [Test]
    public void TryNormalize_RelativePath_ReturnsRootedFullPathWithoutError()
    {
        var result = PathNormalizationHelper.TryNormalize("some-relative-folder", out var error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error, Is.Null);
            Assert.That(result, Is.Not.Null);
            Assert.That(Path.IsPathRooted(result), Is.True);
        }
    }

    [Test]
    public void TryNormalize_InvalidPath_ReturnsNullAndInvalidPathFormatError()
    {
        var invalid = OperatingSystem.IsWindows() ? new string('a', 300_000) : "some-folder\0name";

        var result = PathNormalizationHelper.TryNormalize(invalid, out var error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Null);
            Assert.That(error, Is.Not.Null);
        }
        Assert.That(error!.Code, Is.EqualTo(MessageCode.InvalidPathFormat));
    }
}
