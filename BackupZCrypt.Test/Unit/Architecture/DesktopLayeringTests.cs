using BackupZCrypt.Desktop;

namespace BackupZCrypt.Test.Unit.Architecture;

/// <summary>
/// Keeps the desktop front end layered: ViewModels, the models they expose, and the presentation
/// mappers stay free of the UI framework so they can be tested without it, and nothing below the
/// views reaches back up into them.
/// </summary>
/// <remarks>
/// Each folder of the desktop project is an area named by its namespace below
/// <c>BackupZCrypt.Desktop</c>; the root area holds the application entry point and the view
/// locator. The table is exact: an edge the code no longer uses fails as surely as a new one, so it
/// always describes the code and can never pass vacuously. ViewModels own the ports they need, such
/// as the clipboard and folder picker, and Platform implements them with Avalonia.
/// </remarks>
public sealed class DesktopLayeringTests
{
    /// <summary>
    /// The namespace every desktop area sits under.
    /// </summary>
    private const string DesktopNamespace = "BackupZCrypt.Desktop";

    /// <summary>
    /// The name of the area holding the types declared directly in <see cref="DesktopNamespace"/>.
    /// </summary>
    private const string RootArea = "(root)";

    /// <summary>
    /// The desktop areas each area uses. Views and the root are not listed: they assemble the
    /// others and may use any of them.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedAreas = new(StringComparer.Ordinal)
    {
        ["Animations"] = [],
        ["Assets"] = [],
        ["Behaviors"] = [],
        ["Controls"] = [],
        ["Converters"] = ["Assets", "ViewModels"],
        ["Models"] = [],
        ["Platform"] = ["ViewModels"],
        ["Presentation"] = ["Resources"],
        ["Resources"] = [],
        ["ViewModels"] = ["Models", "Presentation", "Resources"],
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

    [Theory]
    [MemberData(nameof(Areas))]
    internal void AreaReferences_StayWithinTheDocumentedDesktopLayers(string area)
    {
        var expected = AllowedAreas[area].ToHashSet(StringComparer.Ordinal);
        var frameworkFree = FrameworkFreeAreas.Contains(area);
        var areaTypes = DesktopTypes().Where(type => AreaOf(type) == area).ToList();

        var references = areaTypes
            .SelectMany(type => TypeReferences.Of(type).Select(used => (Type: type, Used: used)))
            .ToList();
        var usedAreas = references
            .Select(static reference => AreaOf(reference.Used))
            .OfType<string>()
            .Where(usedArea => usedArea != area)
            .ToHashSet(StringComparer.Ordinal);

        var problems = references
            .Where(reference =>
                (AreaOf(reference.Used) is { } usedArea && usedArea != area && !expected.Contains(usedArea))
                || (frameworkFree && IsFrameworkType(reference.Used))
            )
            .Select(static reference => $"{reference.Type.FullName} -> {reference.Used.FullName}")
            .Concat(expected.Except(usedAreas).Select(static unused => $"documented but unused {unused}"))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(areaTypes);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    internal void OnlyTheRoot_WiresTheApplicationTogether()
    {
        var wiring = DesktopTypes()
            .SelectMany(type =>
                TypeReferences
                    .Of(type)
                    .Where(static used => used.Namespace?.StartsWith("BackupZCrypt.Composition", StringComparison.Ordinal) is true)
                    .Select(used => (Area: AreaOf(type), Edge: $"{type.FullName} -> {used.FullName}"))
            )
            .ToList();
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
        var known = AllowedAreas.Keys.Append(RootArea).Append("Views").ToHashSet(StringComparer.Ordinal);

        var unknown = DesktopTypes()
            .Where(static type => type.Namespace?.StartsWith(DesktopNamespace, StringComparison.Ordinal) is true)
            .Where(type => AreaOf(type) is not { } area || !known.Contains(area))
            .Select(static type => type.FullName)
            .ToList();

        Assert.True(unknown.Count == 0, string.Join(Environment.NewLine, unknown));
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
    /// Gets the desktop area a type belongs to, from its namespace.
    /// </summary>
    /// <param name="type">The type to classify.</param>
    /// <returns>The area name, or <see langword="null"/> for a type outside the desktop project.</returns>
    private static string? AreaOf(Type type)
    {
        if (type.Namespace is not { } ns)
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
    /// Determines whether a type belongs to the UI framework.
    /// </summary>
    /// <param name="type">The referenced type.</param>
    /// <returns><see langword="true"/> for an Avalonia type.</returns>
    private static bool IsFrameworkType(Type type)
    {
        return type.Namespace is { } ns
            && (ns == "Avalonia" || ns.StartsWith("Avalonia.", StringComparison.Ordinal));
    }
}
