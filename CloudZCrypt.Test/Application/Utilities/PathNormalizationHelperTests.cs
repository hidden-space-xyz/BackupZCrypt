namespace CloudZCrypt.Test.Application.Utilities;

using CloudZCrypt.Application.Utilities.Helpers;

[TestFixture]
internal sealed class PathNormalizationHelperTests
{
    [Test]
    public void TryNormalize_ValidPath_ReturnsNormalized()
    {
        string? result = PathNormalizationHelper.TryNormalize(
            @"C:\temp\file.txt",
            out string? error);

        Assert.That(error, Is.Null);
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("file.txt"));
    }

    [Test]
    public void TryNormalize_EmptyPath_ReturnsEmpty()
    {
        string? result = PathNormalizationHelper.TryNormalize(string.Empty, out string? error);

        Assert.That(error, Is.Null);
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void TryNormalize_WhitespacePath_ReturnsEmpty()
    {
        string? result = PathNormalizationHelper.TryNormalize("   ", out string? error);

        Assert.That(error, Is.Null);
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void TryNormalize_InvalidPath_ReturnsNullAndError()
    {
        string? result = PathNormalizationHelper.TryNormalize(
            new string('\0', 5),
            out string? error);

        Assert.That(result, Is.Null);
        Assert.That(error, Is.Not.Null);
        Assert.That(error, Does.StartWith("Invalid path:"));
    }
}
