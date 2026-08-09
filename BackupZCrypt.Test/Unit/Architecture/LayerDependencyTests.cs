using System.Xml.Linq;

using BackupZCrypt.Test.Tools;

namespace BackupZCrypt.Test.Unit.Architecture;

/// <summary>
/// Enforces the dependency rules CLAUDE.md calls non-negotiable, so a violation fails the build
/// rather than surviving until someone re-reads the documentation.
/// </summary>
/// <remarks>
/// <para>
/// The rules are checked by parsing each <c>.csproj</c> rather than by reflecting over
/// <c>Assembly.GetReferencedAssemblies()</c>. Roslyn omits a reference the compiled code never
/// touches, so a reflection-based check would pass on a freshly added illegal reference and only
/// start failing once someone used it — which is exactly when it is too late to be useful.
/// </para>
/// <para>
/// The sibling rules that already had enforcement stay where they are:
/// <c>LocalizationParityTests</c> covers the message catalogue and <c>StrategyRegistrationTests</c>
/// covers the composition root.
/// </para>
/// </remarks>
public sealed class LayerDependencyTests
{
    /// <summary>
    /// The directory separators an MSBuild <c>Include</c> attribute may use, recognized on every platform.
    /// </summary>
    private static readonly char[] ProjectPathSeparators = ['\\', '/'];

    /// <summary>
    /// The project references each project is allowed to declare, straight from the CLAUDE.md table.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedProjectReferences = new(
        StringComparer.Ordinal
    )
    {
        ["BackupZCrypt.Domain"] = [],
        ["BackupZCrypt.Application"] = ["BackupZCrypt.Domain"],
        ["BackupZCrypt.Infrastructure"] = ["BackupZCrypt.Domain"],
        ["BackupZCrypt.Composition"] = ["BackupZCrypt.Application", "BackupZCrypt.Infrastructure"],
        ["BackupZCrypt.Desktop"] = ["BackupZCrypt.Application", "BackupZCrypt.Composition"],
        ["BackupZCrypt.Test"] =
        [
            "BackupZCrypt.Domain",
            "BackupZCrypt.Application",
            "BackupZCrypt.Infrastructure",
            "BackupZCrypt.Composition",
            "BackupZCrypt.Desktop",
        ],
    };

    /// <summary>
    /// Gets the projects under the dependency rules, as the theory data that runs the reference
    /// check once per project.
    /// </summary>
    public static TheoryData<string> Projects => new(AllowedProjectReferences.Keys);

    [Theory]
    [MemberData(nameof(Projects))]
    internal void ProjectReferences_MatchTheDocumentedLayerTable(string project)
    {
        var declared = ReadProject(project)
            .Descendants("ProjectReference")
            .Select(static r => ProjectNameOf(r.Attribute("Include")!.Value))
            .ToList();

        Assert.Equivalent(AllowedProjectReferences[project], declared, strict: true);
    }

    [Fact]
    internal void Domain_DeclaresNoRuntimeNuGetPackage()
    {
        var runtimePackages = ReadProject("BackupZCrypt.Domain")
            .Descendants("PackageReference")
            .Where(static p => !IsBuildTimeOnly(p))
            .Select(static p => p.Attribute("Include")!.Value)
            .ToList();

        Assert.Empty(runtimePackages);
    }

    [Fact]
    internal void SharedBuildProperties_AddOnlyBuildTimeOnlyPackages()
    {
        var leaked = ReadSharedBuildProperties()
            .Descendants("PackageReference")
            .Where(static p => !IsBuildTimeOnly(p))
            .Select(static p => p.Attribute("Include")!.Value)
            .ToList();

        Assert.Empty(leaked);
    }

    [Fact]
    internal void NoProjectPinsItsOwnPackageVersion()
    {
        var pinned = new List<string>();

        foreach (var project in AllowedProjectReferences.Keys)
        {
            pinned.AddRange(
                ReadProject(project)
                    .Descendants("PackageReference")
                    .Where(static p => p.Attribute("Version") is not null)
                    .Select(p => $"{project}: {p.Attribute("Include")!.Value}")
            );
        }

        Assert.Empty(pinned);
    }

    /// <summary>
    /// Extracts the referenced project's name from the <c>Include</c> path of a
    /// <c>ProjectReference</c>.
    /// </summary>
    /// <remarks>
    /// MSBuild writes these paths with backslashes on every platform, but <c>\</c> is a legal
    /// file-name character on Unix, so <see cref="Path.GetFileNameWithoutExtension(string)"/> would
    /// treat the whole path as one name there and return <c>..\BackupZCrypt.Domain\BackupZCrypt.Domain</c>
    /// instead of <c>BackupZCrypt.Domain</c>. Splitting on both separators keeps the check reading
    /// the same edge on Windows and on CI.
    /// </remarks>
    /// <param name="include">The raw <c>Include</c> attribute value.</param>
    /// <returns>The referenced project's name, without directories or extension.</returns>
    private static string ProjectNameOf(string include)
    {
        var lastSegment = include.Split(ProjectPathSeparators)[^1];

        return Path.GetFileNameWithoutExtension(lastSegment);
    }

    /// <summary>
    /// Loads a project file from the repository root.
    /// </summary>
    /// <param name="project">The project name, without extension.</param>
    /// <returns>The parsed project file.</returns>
    private static XDocument ReadProject(string project)
    {
        var path = Path.Combine(RepositoryRoot, project, project + ".csproj");

        Assert.True(File.Exists(path), $"Could not find {path}.");
        return XDocument.Load(path);
    }

    /// <summary>
    /// Loads the solution-wide <c>Directory.Build.props</c>.
    /// </summary>
    /// <returns>The parsed properties file.</returns>
    private static XDocument ReadSharedBuildProperties()
    {
        var path = Path.Combine(RepositoryRoot, "Directory.Build.props");

        Assert.True(File.Exists(path), $"Could not find {path}.");
        return XDocument.Load(path);
    }

    /// <summary>
    /// Gets the repository root, reusing the lookup the format fixtures already perform.
    /// </summary>
    private static string RepositoryRoot => OnDiskFormatFixtures.RepositoryRoot;

    /// <summary>
    /// Determines whether a package reference is a build-time-only asset that ships nothing.
    /// </summary>
    /// <param name="package">The <c>PackageReference</c> element to inspect.</param>
    /// <returns><see langword="true"/> when the reference declares <c>PrivateAssets=all</c>.</returns>
    private static bool IsBuildTimeOnly(XElement package)
    {
        var asAttribute = package.Attribute("PrivateAssets")?.Value;
        var asElement = package.Element("PrivateAssets")?.Value;

        return string.Equals(asAttribute, "all", StringComparison.OrdinalIgnoreCase)
            || string.Equals(asElement, "all", StringComparison.OrdinalIgnoreCase);
    }
}
