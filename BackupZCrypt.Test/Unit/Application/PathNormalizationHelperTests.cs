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
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    internal void TryNormalize_EmptyOrWhitespace_ReturnsEmptyWithoutError(string rawPath)
    {
        var result = PathNormalizationHelper.TryNormalize(rawPath, out var error);

        Assert.Multiple(
            () => Assert.Equal(string.Empty, result),
            () => Assert.Null(error)
        );
    }

    [Fact]
    internal void TryNormalize_RelativePath_IsRejectedInsteadOfResolvedAgainstTheWorkingDirectory()
    {
        var result = PathNormalizationHelper.TryNormalize("some-relative-folder", out var error);

        Assert.Multiple(
            () => Assert.Null(result),
            () => Assert.Equal(MessageCode.PathMustBeAbsoluteFormat, error?.Code),
            () => Assert.Equal<object>(["some-relative-folder"], error!.Args)
        );
    }

    [Fact]
    internal void TryNormalize_PathWrappedInQuotes_IsUnwrapped()
    {
        var folder = Path.Join(Path.GetTempPath(), "quoted folder");

        var result = PathNormalizationHelper.TryNormalize($"  \"{folder}\"  ", out var error);

        Assert.Multiple(
            () => Assert.Null(error),
            () => Assert.Equal(Path.GetFullPath(folder), result)
        );
    }

    [Theory]
    [InlineData("bad<name")]
    [InlineData("what?")]
    [InlineData("a|b")]
    [InlineData("colon:inside")]
    internal void TryNormalize_OnWindows_RejectsCharactersTheFileSystemRefuses(string segment)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Only Windows refuses these characters in a path.");
        }

        var result = PathNormalizationHelper.TryNormalize(
            Path.Join(Path.GetTempPath(), segment),
            out var error
        );

        Assert.Multiple(
            () => Assert.Null(result),
            () => Assert.Equal(MessageCode.PathInvalidCharactersFormat, error?.Code)
        );
    }

    [Fact]
    internal void TryNormalize_PaddedPathWithEnvironmentVariable_TrimsAndExpandsBeforeResolving()
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

            Assert.Multiple(
                () => Assert.Null(error),
                () => Assert.Equal(Path.GetFullPath(variableValue), result)
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    [Fact]
    internal void TryNormalize_UnresolvablePath_ReturnsNullAndAnError()
    {
        var invalid = OperatingSystem.IsWindows() ? new string('a', 300_000) : "some-folder\0name";

        var result = PathNormalizationHelper.TryNormalize(invalid, out var error);

        Assert.Multiple(
            () => Assert.Null(result),
            () => Assert.NotNull(error)
        );

        Assert.Equal(MessageCode.PathMustBeAbsoluteFormat, error!.Code);
    }
}
