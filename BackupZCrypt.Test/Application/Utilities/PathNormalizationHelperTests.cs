namespace BackupZCrypt.Test.Application.Utilities;

using BackupZCrypt.Application.Utilities.Helpers;

[TestFixture]
internal sealed class PathNormalizationHelperTests
{
    [Test]
    public void TryNormalize_ValidPath_ReturnsNormalized()
    {
        var result = PathNormalizationHelper.TryNormalize(
            @"C:\temp\file.txt",
            out var error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error, Is.Null);
            Assert.That(result, Is.Not.Null);
        }
        Assert.That(result, Does.Contain("file.txt"));
    }

    [Test]
    public void TryNormalize_EmptyPath_ReturnsEmpty()
    {
        var result = PathNormalizationHelper.TryNormalize(string.Empty, out var error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error, Is.Null);
            Assert.That(result, Is.EqualTo(string.Empty));
        }
    }

    [Test]
    public void TryNormalize_WhitespacePath_ReturnsEmpty()
    {
        var result = PathNormalizationHelper.TryNormalize("   ", out var error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error, Is.Null);
            Assert.That(result, Is.EqualTo(string.Empty));
        }
    }

    [Test]
    public void TryNormalize_InvalidPath_ReturnsNullAndError()
    {
        var result = PathNormalizationHelper.TryNormalize(
            new string('\0', 5),
            out var error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Null);
            Assert.That(error, Is.Not.Null);
        }
        Assert.That(error, Does.StartWith("Invalid path:"));
    }
}
