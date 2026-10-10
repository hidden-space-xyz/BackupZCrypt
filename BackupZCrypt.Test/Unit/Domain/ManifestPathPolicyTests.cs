using BackupZCrypt.Domain.Archive;

namespace BackupZCrypt.Test.Unit.Domain;

/// <summary>
/// Unit tests for <see cref="ManifestPathPolicy"/>, the rule that decides whether a manifest entry
/// path may be written into an archive and read back out of one.
/// </summary>
/// <remarks>
/// This is the check that keeps a crafted manifest from carrying a path that could steer a restore
/// outside the destination directory; <see cref="BackupZCrypt.Test.Unit.Application.RestorePathGuardTests"/> covers the
/// host-side half. It used to be a set of private statics inside a 2000-line service, reachable only
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
            "/absolute.txt",
            "\\absolute.txt",
            "nul\0char.txt",
            "docs\\notes.md",
            "café.txt",
        };
    }

    /// <summary>
    /// Entry paths that are structurally sound but name something Windows cannot create.
    /// </summary>
    /// <returns>One Windows-incompatible path per case.</returns>
    public static TheoryData<string> WindowsIncompatiblePaths()
    {
        return new()
        {
            "CON",
            "CON .txt",
            "aux.txt",
            "src/aux/x.c",
            "COM1.log",
            "name.",
            "name ",
            "bad:name.txt",
            "bad<name>.txt",
            "a|b",
            "what?",
            "star*",
            "quote\"d",
            "tab\tname",
            "C:/drive-qualified.txt",
        };
    }

    [Theory]
    [MemberData(nameof(WindowsIncompatiblePaths))]
    internal void ValidateRelative_NamesOnlyWindowsRefuses_AreStructurallyValidOnEveryPlatform(string path)
    {
        Assert.Null(Record.Exception(() => ManifestPathPolicy.ValidateRelative(path)));
    }

    [Theory]
    [MemberData(nameof(WindowsIncompatiblePaths))]
    internal void IsValidWindowsPath_NamesWindowsRefuses_AreReportedAsIncompatible(string path)
    {
        Assert.False(ManifestPathPolicy.IsValidWindowsPath(path));
    }

    [Theory]
    [InlineData("docs/notes.md")]
    [InlineData("auxiliary.txt")]
    [InlineData("con-artist/console.log")]
    [InlineData(".gitignore")]
    [InlineData("caf\u00E9.txt")]
    internal void IsValidWindowsPath_OrdinaryNames_AreAccepted(string path)
    {
        Assert.True(ManifestPathPolicy.IsValidWindowsPath(path));
    }

    [Theory]
    [MemberData(nameof(RejectedPaths))]
    internal void ValidateRelative_TraversalEmptyAndNonCanonicalPaths_AreRejectedOnEveryPlatform(string path)
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
    [InlineData("docs/sub/deep.txt")]
    [InlineData("café.txt")]
    [InlineData("a.b/c..d/e.txt")]
    internal void ValidateRelative_OrdinaryRelativePaths_AreAccepted(string path)
    {
        Assert.Null(Record.Exception(() => ManifestPathPolicy.ValidateRelative(path)));
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

    [Fact]
    internal void ToPlatformPath_YieldsHostSeparators()
    {
        var platformPath = ManifestPathPolicy.ToPlatformPath("docs/sub/deep.txt");

        Assert.Equal(
            string.Join(Path.DirectorySeparatorChar, "docs", "sub", "deep.txt"),
            platformPath
        );
    }

    [Fact]
    internal void ToManifestPathThenToPlatformPath_RoundTripsAnEntry()
    {
        var original = Path.Join("docs", "sub", "deep.txt");

        var roundTripped = ManifestPathPolicy.ToPlatformPath(
            ManifestPathPolicy.ToManifestPath(original)
        );

        Assert.Equal(original, roundTripped);
    }
}
