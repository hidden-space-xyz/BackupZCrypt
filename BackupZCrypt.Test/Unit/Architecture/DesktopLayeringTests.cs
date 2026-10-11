using System.Text.RegularExpressions;

using BackupZCrypt.Desktop;
using BackupZCrypt.Test.Tools;

namespace BackupZCrypt.Test.Unit.Architecture;

/// <summary>
/// Keeps the desktop front end layered: ViewModels, the models they expose, and the presentation
/// mappers stay free of the UI framework so they can be tested without it, the views use only what
/// they present, and nothing below the views reaches back up into them.
/// </summary>
/// <remarks>
/// <para>
/// Each folder of the desktop project is an area named by its namespace below
/// <c>BackupZCrypt.Desktop</c>; the root area holds the application entry point, its markup, and the
/// view locator. The table is exact: an edge the code no longer uses fails as surely as a new one, so
/// it always describes the code and can never pass vacuously. ViewModels own the ports they need,
/// such as the clipboard and folder picker, and Platform implements them with Avalonia; only the root
/// wires the two together.
/// </para>
/// <para>
/// Each area is read three ways, because none of them sees every reference. The compiled view reads
/// every type the code uses. The <c>using</c> directives catch what compilation erases, such as a
/// documentation <c>cref</c> or a <c>nameof</c>. The markup view reads the XAML namespaces each
/// <c>.axaml</c> file declares and uses, and the <c>avares://</c> resources it loads, since compiled
/// XAML keeps much of what it references in generated types outside the desktop namespaces.
/// </para>
/// </remarks>
public sealed partial class DesktopLayeringTests
{
    /// <summary>
    /// The namespace every desktop area sits under.
    /// </summary>
    private const string DesktopNamespace = "BackupZCrypt.Desktop";

    /// <summary>
    /// The folder of the desktop project below the repository root.
    /// </summary>
    private const string DesktopProject = "BackupZCrypt.Desktop";

    /// <summary>
    /// The name of the area holding the types declared directly in <see cref="DesktopNamespace"/> and the
    /// files at the project root.
    /// </summary>
    private const string RootArea = "(root)";

    /// <summary>
    /// The desktop areas each area uses.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedAreas = new(StringComparer.Ordinal)
    {
        [RootArea] = ["Platform", "Styles", "ViewModels", "Views"],
        ["Animations"] = [],
        ["Assets"] = [],
        ["Behaviors"] = [],
        ["Controls"] = [],
        ["Converters"] = ["Assets", "ViewModels"],
        ["Models"] = [],
        ["Platform"] = ["ViewModels"],
        ["Presentation"] = ["Resources"],
        ["Resources"] = [],
        ["Styles"] = [],
        ["ViewModels"] = ["Models", "Presentation", "Resources"],
        ["Views"] = ["Animations", "Assets", "Behaviors", "Controls", "Converters", "Models", "Resources", "ViewModels"],
    };

    /// <summary>
    /// The areas that must not use the UI framework, so they run in a plain test process.
    /// </summary>
    private static readonly HashSet<string> FrameworkFreeAreas = new(StringComparer.Ordinal)
    {
        "Models",
        "Presentation",
        "Resources",
        "ViewModels",
    };

    /// <summary>
    /// Gets the areas under the rules, as the theory data that runs the check once per area.
    /// </summary>
    public static TheoryData<string> Areas => new(AllowedAreas.Keys);

    /// <summary>
    /// Gets the folder of the desktop project.
    /// </summary>
    private static string ProjectRoot => Path.Join(OnDiskFormatFixtures.RepositoryRoot, DesktopProject);

    [Theory]
    [MemberData(nameof(Areas))]
    internal void AreaReferences_StayWithinTheDocumentedDesktopLayers(string area)
    {
        var expected = AllowedAreas[area].ToHashSet(StringComparer.Ordinal);
        var areaTypes = DesktopTypes().Where(type => AreaOf(type.Namespace) == area).ToList();
        var areaFiles = SourceFiles().Where(file => AreaOfFile(file) == area).ToList();

        var references = CompiledReferences(areaTypes)
            .Concat(areaFiles.SelectMany(SourceReferences))
            .Where(reference => reference.Area != area)
            .ToList();
        var frameworkUses = FrameworkFreeAreas.Contains(area)
            ? areaTypes.SelectMany(type =>
                TypeReferences
                    .Of(type)
                    .Where(IsFrameworkType)
                    .Select(used => $"framework {used.FullName} from {type.FullName}")
            )
            : [];

        var problems = references
            .Where(reference => !expected.Contains(reference.Area))
            .Select(static reference => $"unexpected {reference.Area} from {reference.Origin}")
            .Concat(frameworkUses)
            .Concat(
                expected
                    .Except(references.Select(static reference => reference.Area), StringComparer.Ordinal)
                    .Select(static unused => $"documented but unused {unused}")
            )
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(areaTypes.Count + areaFiles.Count > 0, $"Nothing sits in {area}.");
        Assert.True(problems.Count == 0, $"{area}:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    [Fact]
    internal void OnlyTheRoot_WiresTheApplicationTogether()
    {
        var compiled = DesktopTypes()
            .SelectMany(type =>
                TypeReferences
                    .Of(type)
                    .Where(static used => used.Namespace?.StartsWith("BackupZCrypt.Composition", StringComparison.Ordinal) is true)
                    .Select(used => (Area: AreaOf(type.Namespace), Edge: $"{type.FullName} -> {used.FullName}"))
            );
        var mentioned = SourceFiles()
            .Where(static file => CompositionMention().IsMatch(File.ReadAllText(file)))
            .Select(static file => (Area: (string?)AreaOfFile(file), Edge: Path.GetRelativePath(ProjectRoot, file)));
        var wiring = compiled.Concat(mentioned).ToList();

        var outsideTheRoot = wiring
            .Where(static reference => reference.Area is not RootArea)
            .Select(static reference => reference.Edge)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Contains(wiring, static reference => reference.Area is RootArea);
        Assert.True(outsideTheRoot.Count == 0, string.Join(Environment.NewLine, outsideTheRoot));
    }

    [Fact]
    internal void EveryDesktopType_BelongsToAnArea()
    {
        var unknown = DesktopTypes()
            .Where(static type => type.Namespace?.StartsWith(DesktopNamespace, StringComparison.Ordinal) is true)
            .Where(static type => AreaOf(type.Namespace) is not { } area || !AllowedAreas.ContainsKey(area))
            .Select(static type => type.FullName)
            .ToList();

        Assert.True(unknown.Count == 0, string.Join(Environment.NewLine, unknown));
    }

    [Fact]
    internal void EveryDesktopSourceFile_SitsInAnArea()
    {
        var misplaced = SourceFiles()
            .Where(static file => !AllowedAreas.ContainsKey(AreaOfFile(file)))
            .Select(static file => Path.GetRelativePath(ProjectRoot, file))
            .ToList();

        Assert.True(misplaced.Count == 0, string.Join(Environment.NewLine, misplaced));
    }

    [Fact]
    internal void EveryXamlNamespaceDeclaration_IsUsedByItsFile()
    {
        var files = SourceFiles().Where(static file => file.EndsWith(".axaml", StringComparison.Ordinal)).ToList();

        var unused = files
            .SelectMany(static file =>
            {
                var markup = File.ReadAllText(file);

                return XamlNamespaceDeclaration()
                    .Matches(markup)
                    .Select(static declaration => declaration.Groups["prefix"].Value)
                    .Where(prefix => !IsPrefixUsed(markup, prefix))
                    .Select(prefix => $"{Path.GetRelativePath(ProjectRoot, file)} declares {prefix}");
            })
            .ToList();

        Assert.NotEmpty(files);
        Assert.True(unused.Count == 0, string.Join(Environment.NewLine, unused));
    }

    /// <summary>
    /// Lists the desktop areas the given compiled types reference.
    /// </summary>
    /// <param name="types">The types whose references are read.</param>
    /// <returns>Each referenced area with the type that references it.</returns>
    private static IEnumerable<(string Area, string Origin)> CompiledReferences(IEnumerable<Type> types)
    {
        return types.SelectMany(static type =>
            TypeReferences
                .Of(type)
                .Select(static used => AreaOf(used.Namespace))
                .OfType<string>()
                .Select(area => (area, type.FullName ?? type.Name))
        );
    }

    /// <summary>
    /// Lists the desktop areas a C# or XAML source file references: the namespaces its <c>using</c>
    /// directives import, the XAML namespaces it declares and uses, and the folders of the
    /// <c>avares://</c> resources it loads.
    /// </summary>
    /// <param name="file">The full path of the file.</param>
    /// <returns>Each referenced area with the file that references it.</returns>
    private static IEnumerable<(string Area, string Origin)> SourceReferences(string file)
    {
        var text = File.ReadAllText(file);
        var origin = Path.GetRelativePath(ProjectRoot, file);

        var imported = UsingDirective()
            .Matches(text)
            .Select(static directive => AreaOf(directive.Groups["namespace"].Value));
        var declared = XamlNamespaceDeclaration()
            .Matches(text)
            .Where(declaration => IsPrefixUsed(text, declaration.Groups["prefix"].Value))
            .Select(static declaration => AreaOf(declaration.Groups["namespace"].Value));
        var loaded = AssetUri()
            .Matches(text)
            .Where(static uri => uri.Groups["assembly"].Value == typeof(App).Assembly.GetName().Name)
            .Select(static uri => (string?)uri.Groups["area"].Value);

        return imported
            .Concat(declared)
            .Concat(loaded)
            .OfType<string>()
            .Select(area => (area, origin));
    }

    /// <summary>
    /// Determines whether a XAML file uses a namespace prefix it declares, in an element, an
    /// attribute, a markup extension, or a type name as <c>prefix:Name</c>, or in a style selector as
    /// <c>prefix|Name</c>.
    /// </summary>
    /// <param name="markup">The text of the XAML file.</param>
    /// <param name="prefix">The declared prefix.</param>
    /// <returns><see langword="true"/> when the prefix qualifies a name anywhere in the file.</returns>
    private static bool IsPrefixUsed(string markup, string prefix)
    {
        return Regex.IsMatch(
            markup,
            $@"(?<![\w.:|]){Regex.Escape(prefix)}[:|][A-Za-z_]",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5)
        );
    }

    /// <summary>
    /// Lists every type compiled into the desktop assembly.
    /// </summary>
    /// <returns>The types.</returns>
    private static Type[] DesktopTypes()
    {
        return typeof(App).Assembly.GetTypes();
    }

    /// <summary>
    /// Lists the C# and XAML source files of the desktop project, leaving out build output.
    /// </summary>
    /// <returns>The full paths of the files.</returns>
    private static List<string> SourceFiles()
    {
        Assert.True(Directory.Exists(ProjectRoot), $"Could not find {ProjectRoot}.");

        return
        [
            .. Directory
                .EnumerateFiles(ProjectRoot, "*.*", SearchOption.AllDirectories)
                .Where(static file => Path.GetExtension(file) is ".cs" or ".axaml")
                .Where(static file =>
                    Path.GetRelativePath(ProjectRoot, file).Split(Path.DirectorySeparatorChar)[0] is not ("bin" or "obj")
                ),
        ];
    }

    /// <summary>
    /// Gets the desktop area a namespace belongs to.
    /// </summary>
    /// <param name="ns">The namespace to classify.</param>
    /// <returns>The area name, or <see langword="null"/> for a namespace outside the desktop project.</returns>
    private static string? AreaOf(string? ns)
    {
        if (ns is null)
        {
            return null;
        }

        if (ns == DesktopNamespace)
        {
            return RootArea;
        }

        if (!ns.StartsWith(DesktopNamespace + ".", StringComparison.Ordinal))
        {
            return null;
        }

        var rest = ns[(DesktopNamespace.Length + 1)..];
        var separator = rest.IndexOf('.', StringComparison.Ordinal);

        return separator < 0 ? rest : rest[..separator];
    }

    /// <summary>
    /// Gets the desktop area a source file belongs to, from the folder it sits in.
    /// </summary>
    /// <param name="file">The full path of the file.</param>
    /// <returns>The top-level folder of the file, or the root area for a file at the project root.</returns>
    private static string AreaOfFile(string file)
    {
        return Path.GetRelativePath(ProjectRoot, file).Split(Path.DirectorySeparatorChar) is [var folder, _, ..]
            ? folder
            : RootArea;
    }

    /// <summary>
    /// Determines whether a type belongs to the UI framework.
    /// </summary>
    /// <param name="type">The referenced type.</param>
    /// <returns><see langword="true"/> for an Avalonia type.</returns>
    private static bool IsFrameworkType(Type type)
    {
        return type.Namespace is { } ns
            && (ns == "Avalonia" || ns.StartsWith("Avalonia.", StringComparison.Ordinal));
    }

    /// <summary>
    /// Matches a <c>using</c> directive, plain, static, global, or aliased, and captures the
    /// namespace or type it names.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(
        @"^\s*(?:global\s+)?using\s+(?:static\s+)?(?:\w+\s*=\s*)?(?<namespace>[\w.]+)\s*;",
        RegexOptions.Multiline | RegexOptions.CultureInvariant
    )]
    private static partial Regex UsingDirective();

    /// <summary>
    /// Matches a XAML namespace declaration that maps a prefix to a CLR namespace, and captures both.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(
        @"xmlns:(?<prefix>\w+)\s*=\s*""(?:using:|clr-namespace:)(?<namespace>[\w.]+)",
        RegexOptions.CultureInvariant
    )]
    private static partial Regex XamlNamespaceDeclaration();

    /// <summary>
    /// Matches an <c>avares://</c> resource and captures the assembly it is embedded in and the
    /// top-level folder it is loaded from.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(@"avares://(?<assembly>[^/""]+)/(?<area>[A-Za-z]+)/", RegexOptions.CultureInvariant)]
    private static partial Regex AssetUri();

    /// <summary>
    /// Matches a mention of the composition root's namespace.
    /// </summary>
    /// <returns>The compiled expression.</returns>
    [GeneratedRegex(@"\bBackupZCrypt\.Composition\b", RegexOptions.CultureInvariant)]
    private static partial Regex CompositionMention();
}
