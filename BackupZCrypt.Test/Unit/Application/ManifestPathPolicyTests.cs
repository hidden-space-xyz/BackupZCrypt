using BackupZCrypt.Application.Utilities.Helpers;
using BackupZCrypt.Test.Common;
using BackupZCrypt.Infrastructure.Services;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for <see cref="ManifestPathPolicy"/>, the rule that decides whether a manifest entry
/// path may be written and where a restore is allowed to put it.
/// </summary>
/// <remarks>
/// This is the check that keeps a crafted manifest from steering a restore outside the destination
/// directory. It used to be a set of private statics inside a 2000-line service, reachable only
/// through a full backup round trip; testing it directly is the point of having extracted it.
/// </remarks>
public sealed class ManifestPathPolicyTests
{
    /// <summary>
    /// Entry paths that must be rejected however they arrive, in either separator notation.
    /// </summary>
    /// <returns>One rejected path per case.</returns>
    public static TheoryData<string> RejectedPaths()
    {
        return new()
        {
            string.Empty,
            "   ",
            "../escape.txt",
            "..\\escape.txt",
            "docs/../../escape.txt",
            "docs\\..\\..\\escape.txt",
            "a/../../b/../../escape.txt",
            ".",
            "./file.txt",
            "docs/./file.txt",
            "docs//file.txt",
            "docs\\\\file.txt",
            "CON",
            "CON .txt",
            "aux.txt",
            "name.",
            "name ",
            "bad:name.txt",
            "bad<name>.txt",
        };
    }

    [Theory]
    [MemberData(nameof(RejectedPaths))]
    internal void ValidateRelative_TraversalAndEmptyPaths_AreRejectedOnEveryPlatform(string path)
    {
        _ = Assert.Throws<InvalidDataException>(() => ManifestPathPolicy.ValidateRelative(path));
    }

    [Fact]
    internal void ValidateRelative_BackslashTraversal_IsRejectedEvenWhereBackslashIsALegalNameCharacter()
    {
        _ = Assert.Throws<InvalidDataException>(
            () => ManifestPathPolicy.ValidateRelative("..\\..\\escape.txt")
        );
    }

    [Theory]
    [InlineData("root.txt")]
    [InlineData("docs/notes.md")]
    [InlineData("docs\\notes.md")]
    [InlineData("docs/sub/deep.txt")]
    [InlineData("a.b/c..d/e.txt")]
    internal void ValidateRelative_OrdinaryRelativePaths_AreAccepted(string path)
    {
        Assert.Null(Record.Exception(() => ManifestPathPolicy.ValidateRelative(path)));
    }

    [Fact]
    internal void ValidateRelative_RootedPath_IsRejected()
    {
        var rooted = Path.Combine(Path.GetTempPath(), "escape.txt");

        _ = Assert.Throws<InvalidDataException>(() => ManifestPathPolicy.ValidateRelative(rooted));
    }

    [Fact]
    internal void ResolveSafeDestination_OrdinaryEntry_LandsInsideTheRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "bzc-policy-root");

        var resolved = ManifestPathPolicy.ResolveSafeDestination(root, "docs/sub/deep.txt");

        Assert.StartsWith(
            Path.GetFullPath(root) + Path.DirectorySeparatorChar,
            resolved,
            StringComparison.Ordinal
        );
    }

    [Fact]
    internal void ResolveSafeDestination_SiblingWhoseNameSharesThePrefix_IsNotTreatedAsInside()
    {
        var root = Path.Combine(Path.GetTempPath(), "bzc-root");

        _ = Assert.Throws<InvalidDataException>(
            () => ManifestPathPolicy.ResolveSafeDestination(root, "../bzc-root-evil/escape.txt")
        );
    }
    [Fact]
    internal void EnsureNoReparsePointDescendants_LinkBelowRoot_IsRejected()
    {
        using var dir = new TempDir();
        var service = new FileOperationsService();
        var root = dir.Combine("root");
        var outside = dir.Combine("outside");
        _ = Directory.CreateDirectory(root);
        _ = Directory.CreateDirectory(outside);
        var link = Path.Combine(root, "linked");

        try
        {
            _ = Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip("This platform refuses directory symbolic links: " + ex.Message);
        }

        _ = Assert.Throws<InvalidDataException>(
            () =>
                ManifestPathPolicy.EnsureNoReparsePointDescendants(
                    service,
                    root,
                    Path.Combine(link, "nested")
                )
        );
    }


    [Fact]
    internal void ToManifestPath_NormalizesWindowsSeparatorsAndRejectsAmbiguousUnixBackslashes()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(
                "docs/sub/deep.txt",
                ManifestPathPolicy.ToManifestPath("docs\\sub\\deep.txt")
            );
        }
        else
        {
            _ = Assert.Throws<InvalidDataException>(
                () => ManifestPathPolicy.ToManifestPath("docs\\sub\\deep.txt")
            );
        }
    }

    [Fact]
    internal void ToManifestPath_NormalizesUnicodeComposition()
    {
        Assert.Equal(
            "caf\u00E9.txt",
            ManifestPathPolicy.ToManifestPath("cafe\u0301.txt")
        );
    }

    [Theory]
    [InlineData("docs/sub/deep.txt")]
    [InlineData("docs\\sub\\deep.txt")]
    internal void ToPlatformPath_AcceptsEitherNotation_AndYieldsHostSeparators(string manifestPath)
    {
        var platformPath = ManifestPathPolicy.ToPlatformPath(manifestPath);

        Assert.Equal(
            string.Join(Path.DirectorySeparatorChar, "docs", "sub", "deep.txt"),
            platformPath
        );
    }

    [Fact]
    internal void ToManifestPathThenToPlatformPath_RoundTripsAnEntry()
    {
        var original = Path.Combine("docs", "sub", "deep.txt");

        var roundTripped = ManifestPathPolicy.ToPlatformPath(
            ManifestPathPolicy.ToManifestPath(original)
        );

        Assert.Equal(original, roundTripped);
    }
}
