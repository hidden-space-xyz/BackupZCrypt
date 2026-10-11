using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using BackupZCrypt.Application.Common;
using BackupZCrypt.Desktop;
using BackupZCrypt.Infrastructure.Services;
using BackupZCrypt.Test.Tools;

namespace BackupZCrypt.Test.Unit.Architecture;

/// <summary>
/// Pins which feature folders of the Application layer each Application feature references, and
/// which Application features and Domain areas each outer assembly references, so a feature cannot
/// quietly start depending on one that depends on it and an outer assembly cannot reach past the
/// part of the inner layers meant for it.
/// </summary>
/// <remarks>
/// <para>
/// The features form a directed acyclic graph: <c>Algorithms</c>, <c>Common</c>, and the
/// <c>FileSystem</c> ports depend on no feature, the chunked <c>Engine</c> depends only on those and
/// <c>Manifest</c>, and the use-case features sit on top. Infrastructure reaches only the ports it
/// implements and the Domain it implements them for; the desktop front end reaches neither the
/// engine nor the ports, and of the Domain only the algorithm identifiers. Because a port can share
/// a feature namespace with a use case, Desktop is also checked against every contract Infrastructure
/// implements.
/// </para>
/// <para>
/// The tables are exact rather than upper bounds: an edge the code no longer uses fails as surely as
/// a new one, so the tables always describe the code and can never pass vacuously. Each assembly is
/// read twice, because neither view sees every reference. The compiled view reads every type the
/// signatures, attributes, generic constraints, and method bodies use, however the name was spelled.
/// The source view reads every mention of a feature namespace, which also catches what compilation
/// erases: a documentation <c>cref</c>, an unused <c>using</c>, a <c>nameof</c>, or an inlined
/// constant.
/// </para>
/// </remarks>
public sealed partial class FeatureDependencyTests
{
    /// <summary>
    /// The project whose feature folders are checked.
    /// </summary>
    private const string ApplicationProject = "BackupZCrypt.Application";

    /// <summary>
    /// The namespace every Application feature namespace sits directly under.
    /// </summary>
    private const string ApplicationNamespace = "BackupZCrypt.Application";

    /// <summary>
    /// The features each Application feature references: the single source of truth for these
    /// rules, which AGENTS.md points to.
    /// </summary>
    private static readonly Dictionary<string, string[]> FeatureReferences = new(
        StringComparer.Ordinal
    )
    {
        ["Algorithms"] = [],
        ["Common"] = [],
        ["FileSystem"] = [],
        ["Manifest"] = ["Algorithms", "Common", "FileSystem"],
        ["Engine"] = ["Algorithms", "Common", "FileSystem", "Manifest"],
        ["Passwords"] = ["Common"],
        ["Settings"] = ["Algorithms", "Common"],
        ["Benchmark"] = ["Algorithms", "Common", "Engine"],
        ["Backup"] = ["Algorithms", "Common", "Engine", "FileSystem", "Manifest", "Passwords"],
    };

    /// <summary>
    /// The Application features and Domain areas each outer project references, each named by its
    /// namespace below <c>BackupZCrypt</c>. The composition root is not listed: wiring every
    /// implementation to its contract is its job.
    /// </summary>
    private static readonly Dictionary<string, string[]> OuterReferences = new(StringComparer.Ordinal)
    {
        ["BackupZCrypt.Infrastructure"] =
        [
            "Application.FileSystem",
            "Application.Settings",
            "Domain.Archive",
            "Domain.Constants",
            "Domain.Enums",
            "Domain.Strategies",
        ],
        ["BackupZCrypt.Desktop"] =
        [
            "Application.Algorithms",
            "Application.Backup",
            "Application.Benchmark",
            "Application.Common",
            "Application.Manifest",
            "Application.Passwords",
            "Application.Settings",
            "Domain.Enums",
        ],
    };

    /// <summary>
    /// The compiled assembly of each outer project.
    /// </summary>
    private static readonly Dictionary<string, Assembly> OuterAssembliesByProject = new(
        StringComparer.Ordinal
    )
    {
        ["BackupZCrypt.Infrastructure"] = typeof(FileOperationsService).Assembly,
        ["BackupZCrypt.Desktop"] = typeof(App).Assembly,
    };

    /// <summary>
    /// Gets the Application features under the rules, as the theory data that runs the check once
    /// per feature.
    /// </summary>
    public static TheoryData<string> Features => new(FeatureReferences.Keys);

    /// <summary>
    /// Gets the outer projects under the rules, as the theory data that runs the check once per project.
    /// </summary>
    public static TheoryData<string> OuterProjects => new(OuterReferences.Keys);

    [Theory]
    [MemberData(nameof(Features))]
    internal void FeatureReferences_MatchTheDocumentedFeatureRules(string feature)
    {
        var found = CompiledReferences(ApplicationTypes().Where(type => FeatureOf(type) == feature), FeatureOf)
            .Concat(SourceReferences(FeatureFolder(feature), RelativeFeatureNamespace()))
            .Where(reference => reference.Feature != feature);

        AssertReferencesMatch(feature, FeatureReferences[feature], found);
    }

    [Theory]
    [MemberData(nameof(OuterProjects))]
    internal void OuterProjectReferences_MatchTheDocumentedRules(string project)
    {
        var found = CompiledReferences(OuterAssembliesByProject[project].GetTypes(), AreaOf)
            .Concat(
                SourceReferences(Path.Join(OnDiskFormatFixtures.RepositoryRoot, project), QualifiedArea())
            );

        AssertReferencesMatch(project, OuterReferences[project], found);
    }

    [Fact]
    internal void Desktop_UsesNoContractInfrastructureImplements()
    {
        var implemented = OuterAssembliesByProject["BackupZCrypt.Infrastructure"]
            .GetTypes()
            .SelectMany(static type => type.GetInterfaces())
            .Select(static contract => contract.IsGenericType ? contract.GetGenericTypeDefinition() : contract)
            .Where(static contract => contract.Namespace?.StartsWith("BackupZCrypt.", StringComparison.Ordinal) is true)
            .ToHashSet();

        var violations = OuterAssembliesByProject["BackupZCrypt.Desktop"]
            .GetTypes()
            .SelectMany(type =>
                TypeReferences
                    .Of(type)
                    .Where(implemented.Contains)
                    .Select(contract => $"{type.FullName} -> {contract.FullName}")
            )
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(implemented);
        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    internal void EveryApplicationType_BelongsToAFeatureWithDocumentedRules()
    {
        var undocumented = ApplicationTypes()
            .Where(static type => !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .Where(static type => type.Namespace?.StartsWith(ApplicationNamespace, StringComparison.Ordinal) is true)
            .Where(static type => FeatureOf(type) is not { } feature || !FeatureReferences.ContainsKey(feature))
            .Select(static type => type.FullName)
            .ToList();

        Assert.True(undocumented.Count == 0, string.Join(Environment.NewLine, undocumented));
    }

    [Fact]
    internal void EveryApplicationSourceFile_SitsInAFeatureFolderWithDocumentedRules()
    {
        var projectRoot = Path.Join(OnDiskFormatFixtures.RepositoryRoot, ApplicationProject);

        var misplaced = SourceFiles(projectRoot, "*.cs")
            .Select(file => Path.GetRelativePath(projectRoot, file))
            .Where(static path =>
                path.Split(Path.DirectorySeparatorChar) is not [var folder, _, ..]
                || !FeatureReferences.ContainsKey(folder)
            )
            .ToList();

        Assert.True(misplaced.Count == 0, string.Join(Environment.NewLine, misplaced));
    }

    /// <summary>
    /// Asserts that the features found are exactly the documented ones, listing where each
    /// unexpected reference came from and which documented edges are no longer used.
    /// </summary>
    /// <param name="subject">The feature or project checked.</param>
    /// <param name="documented">The features the rules say it references.</param>
    /// <param name="found">The references found, each with where it was seen.</param>
    private static void AssertReferencesMatch(
        string subject,
        IEnumerable<string> documented,
        IEnumerable<(string Feature, string Origin)> found
    )
    {
        var expected = documented.ToHashSet(StringComparer.Ordinal);
        var references = found.ToList();

        var unexpected = references
            .Where(reference => !expected.Contains(reference.Feature))
            .Select(static reference => $"unexpected {reference.Feature} from {reference.Origin}")
            .Distinct(StringComparer.Ordinal);
        var unused = expected
            .Except(references.Select(static reference => reference.Feature), StringComparer.Ordinal)
            .Select(static feature => $"documented but unused {feature}");
        var problems = unexpected.Concat(unused).Order(StringComparer.Ordinal).ToList();

        Assert.True(
            problems.Count == 0,
            $"{subject}:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}"
        );
    }

    /// <summary>
    /// Lists the features or areas the given compiled types reference.
    /// </summary>
    /// <param name="types">The types whose references are read.</param>
    /// <param name="classify">Names the feature or area a referenced type belongs to, or returns <see langword="null"/> to ignore it.</param>
    /// <returns>Each referenced feature or area with the type that references it.</returns>
    private static IEnumerable<(string Feature, string Origin)> CompiledReferences(
        IEnumerable<Type> types,
        Func<Type, string?> classify
    )
    {
        return types.SelectMany(type =>
            TypeReferences
                .Of(type)
                .Select(classify)
                .OfType<string>()
                .Select(feature => (feature, type.FullName ?? type.Name))
        );
    }

    /// <summary>
    /// Lists the features or areas the C# and XAML source files under a folder mention.
    /// </summary>
    /// <param name="folder">The folder searched, excluding build output.</param>
    /// <param name="pattern">The expression whose <c>feature</c> group names what a mention refers to.</param>
    /// <returns>Each referenced feature or area with the file that mentions it.</returns>
    private static IEnumerable<(string Feature, string Origin)> SourceReferences(string folder, Regex pattern)
    {
        return SourceFiles(folder, "*.cs")
            .Concat(SourceFiles(folder, "*.axaml"))
            .SelectMany(file =>
                pattern
                    .Matches(File.ReadAllText(file))
                    .Select(match => (match.Groups["feature"].Value, Path.GetRelativePath(folder, file)))
            );
    }

    /// <summary>
    /// Lists the source files under a folder, leaving out build output.
    /// </summary>
    /// <param name="folder">The folder searched.</param>
    /// <param name="pattern">The file name pattern.</param>
    /// <returns>The full paths of the matching files.</returns>
    private static IEnumerable<string> SourceFiles(string folder, string pattern)
    {
        Assert.True(Directory.Exists(folder), $"Could not find {folder}.");

        return Directory
            .EnumerateFiles(folder, pattern, SearchOption.AllDirectories)
            .Where(file =>
                Path.GetRelativePath(folder, file).Split(Path.DirectorySeparatorChar)[0] is not ("bin" or "obj")
            );
    }

    /// <summary>
    /// Gets the folder of one Application feature.
    /// </summary>
    /// <param name="feature">The feature name.</param>
    /// <returns>The folder's full path.</returns>
    private static string FeatureFolder(string feature)
    {
        return Path.Join(OnDiskFormatFixtures.RepositoryRoot, ApplicationProject, feature);
    }

    /// <summary>
    /// Lists every type compiled into the Application assembly, nested and compiler-generated ones
    /// included.
    /// </summary>
    /// <returns>The types.</returns>
    private static Type[] ApplicationTypes()
    {
        return typeof(Result).Assembly.GetTypes();
    }

    /// <summary>
    /// Gets the Application feature a type belongs to, from its namespace.
    /// </summary>
    /// <param name="type">The type to classify.</param>
    /// <returns>The feature name, or <see langword="null"/> for a type outside every feature namespace.</returns>
    private static string? FeatureOf(Type type)
    {
        var prefix = ApplicationNamespace + ".";

        if (type.Namespace is not { } ns || !ns.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = ns[prefix.Length..];
        var separator = rest.IndexOf('.', StringComparison.Ordinal);

        return separator < 0 ? rest : rest[..separator];
    }

    /// <summary>
    /// Gets the Application feature or Domain area a type belongs to, from its namespace.
    /// </summary>
    /// <param name="type">The type to classify.</param>
    /// <returns>
    /// The namespace below <c>BackupZCrypt</c> cut after its second segment, such as
    /// <c>Application.Backup</c> or <c>Domain.Enums</c>, or <see langword="null"/> for a type outside
    /// both layers.
    /// </returns>
    private static string? AreaOf(Type type)
    {
        return type.Namespace is { } ns && QualifiedArea().Match(ns) is { Success: true, Index: 0 } match
            ? match.Groups["feature"].Value
            : null;
    }

    /// <summary>
    /// Matches a fully qualified mention of an Application feature or Domain area namespace and
    /// captures it below the <c>BackupZCrypt</c> root. Only fully qualified mentions count outside
    /// Application, where a bare <c>Application.</c> belongs to other libraries, such as Avalonia's
    /// <c>Application.Current</c>.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(@"\bBackupZCrypt\.(?<feature>(?:Application|Domain)\.[A-Za-z]+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex QualifiedArea();

    /// <summary>
    /// Matches a mention of an Application feature namespace, fully qualified or relative to the
    /// <c>BackupZCrypt</c> root, and captures the feature.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(@"(?<![\w.])(?:BackupZCrypt\.)?Application\.(?<feature>[A-Za-z]+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex RelativeFeatureNamespace();
}
