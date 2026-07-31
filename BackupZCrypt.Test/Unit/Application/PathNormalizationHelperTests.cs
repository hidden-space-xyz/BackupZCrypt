using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the path normalization helper.
/// </summary>
/// <remarks>
/// The Desktop layer feeds raw text-box input straight into a backup request, so whatever this helper
/// fails to trim, expand, or reject reaches the backup engine as a real path: dropping either the trim or
/// the environment-variable expansion would silently create a directory literally named
/// <c>%USERPROFILE%</c> and put the only copy of the user's backup somewhere they never asked for. The
/// expansion case therefore has to set a real environment variable, and gives it a GUID suffix so it
/// cannot collide with another test or with one the host already defines.
/// </remarks>
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
    public void TryNormalize_RelativePath_ResolvesAgainstTheCurrentDirectory()
    {
        var result = PathNormalizationHelper.TryNormalize("some-relative-folder", out var error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error, Is.Null);
            Assert.That(
                result,
                Is.EqualTo(Path.Combine(Environment.CurrentDirectory, "some-relative-folder"))
            );
        }
    }

    [Test]
    public void TryNormalize_PaddedPathWithEnvironmentVariable_TrimsAndExpandsBeforeResolving()
    {
        var variableName = "BZC_TEST_ROOT_" + Guid.NewGuid().ToString("N");
        var variableValue = Path.GetTempPath();
        Environment.SetEnvironmentVariable(variableName, variableValue);

        try
        {
            var result = PathNormalizationHelper.TryNormalize(
                $"  %{variableName}%  ",
                out var error
            );

            using (Assert.EnterMultipleScope())
            {
                Assert.That(error, Is.Null);
                Assert.That(result, Is.EqualTo(Path.GetFullPath(variableValue)));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
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
