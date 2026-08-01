using BackupZCrypt.Application.Utilities.Helpers;

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
    private static IEnumerable<string> RejectedPaths()
    {
        yield return string.Empty;
        yield return "   ";
        yield return "../escape.txt";
        yield return "..\\escape.txt";
        yield return "docs/../../escape.txt";
        yield return "docs\\..\\..\\escape.txt";
        yield return "a/../../b/../../escape.txt";
    }

    [TestCaseSource(nameof(RejectedPaths))]
    public void ValidateRelative_TraversalAndEmptyPaths_AreRejectedOnEveryPlatform(string path)
    {
        _ = Assert.Throws<InvalidDataException>(() => ManifestPathPolicy.ValidateRelative(path));
    }

    [Test]
    public void ValidateRelative_BackslashTraversal_IsRejectedEvenWhereBackslashIsALegalNameCharacter()
    {
        _ = Assert.Throws<InvalidDataException>(
            () => ManifestPathPolicy.ValidateRelative("..\\..\\escape.txt"),
            "On Unix '\\' is an ordinary file-name character, so a check that split only on the host "
                + "separator would read this as one harmless file name and let it through."
        );
    }

    [TestCase("root.txt")]
    [TestCase("docs/notes.md")]
    [TestCase("docs\\notes.md")]
    [TestCase("docs/sub/deep.txt")]
    [TestCase("a.b/c..d/e.txt")]
    public void ValidateRelative_OrdinaryRelativePaths_AreAccepted(string path)
    {
        Assert.DoesNotThrow(() => ManifestPathPolicy.ValidateRelative(path));
    }

    [Test]
    public void ValidateRelative_RootedPath_IsRejected()
    {
        var rooted = Path.Combine(Path.GetTempPath(), "escape.txt");

        _ = Assert.Throws<InvalidDataException>(() => ManifestPathPolicy.ValidateRelative(rooted));
    }

    [Test]
    public void ResolveSafeDestination_OrdinaryEntry_LandsInsideTheRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "bzc-policy-root");

        var resolved = ManifestPathPolicy.ResolveSafeDestination(root, "docs/sub/deep.txt");

        Assert.That(
            resolved,
            Does.StartWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar),
            "A well-formed entry must resolve inside the destination it was restored into."
        );
    }

    [Test]
    public void ResolveSafeDestination_SiblingWhoseNameSharesThePrefix_IsNotTreatedAsInside()
    {
        var root = Path.Combine(Path.GetTempPath(), "bzc-root");

        _ = Assert.Throws<InvalidDataException>(
            () => ManifestPathPolicy.ResolveSafeDestination(root, "../bzc-root-evil/escape.txt"),
            "'bzc-root-evil' starts with 'bzc-root' as a string but is a different directory; the "
                + "root is compared with a trailing separator precisely so this is not accepted."
        );
    }

    [Test]
    public void ToManifestPath_AlwaysWritesForwardSlashes()
    {
        Assert.That(
            ManifestPathPolicy.ToManifestPath("docs\\sub\\deep.txt"),
            Is.EqualTo("docs/sub/deep.txt"),
            "An archive must record the same entry text whichever platform wrote it."
        );
    }

    [TestCase("docs/sub/deep.txt")]
    [TestCase("docs\\sub\\deep.txt")]
    public void ToPlatformPath_AcceptsEitherNotation_AndYieldsHostSeparators(string manifestPath)
    {
        var platformPath = ManifestPathPolicy.ToPlatformPath(manifestPath);

        Assert.That(
            platformPath,
            Is.EqualTo(
                string.Join(
                    Path.DirectorySeparatorChar,
                    new[] { "docs", "sub", "deep.txt" }
                )
            ),
            "An archive written on either platform must rebuild the same tree on this one."
        );
    }

    [Test]
    public void ToManifestPathThenToPlatformPath_RoundTripsAnEntry()
    {
        var original = Path.Combine("docs", "sub", "deep.txt");

        var roundTripped = ManifestPathPolicy.ToPlatformPath(
            ManifestPathPolicy.ToManifestPath(original)
        );

        Assert.That(roundTripped, Is.EqualTo(original));
    }
}
