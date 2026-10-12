using BackupZCrypt.Domain.Archive;

namespace BackupZCrypt.Test.Unit.Architecture;

/// <summary>
/// Keeps the Domain layer free of I/O, so the archive rules it holds stay pure and every touch of
/// the file system, network, or other processes goes through a port Application owns.
/// </summary>
/// <remarks>
/// <c>System.IO.Path</c> stays allowed: it only manipulates strings. Streams stay allowed too, since
/// the codec reads and writes in-memory buffers through them. The tracker types that coverage
/// instrumentation injects into the assembly write their hits to disk and are not Domain code, so
/// they are skipped.
/// </remarks>
public sealed class DomainPurityTests
{
    /// <summary>
    /// The namespace of the tracker types coverage instrumentation injects into an instrumented assembly.
    /// </summary>
    private const string CoverageInstrumentationNamespace = "Microsoft.CodeCoverage";

    /// <summary>
    /// The types whose use means the Domain is touching the outside world.
    /// </summary>
    private static readonly HashSet<string> ForbiddenTypes = new(StringComparer.Ordinal)
    {
        "System.IO.File",
        "System.IO.FileInfo",
        "System.IO.FileStream",
        "System.IO.FileSystemInfo",
        "System.IO.FileSystemWatcher",
        "System.IO.Directory",
        "System.IO.DirectoryInfo",
        "System.IO.DriveInfo",
        "System.IO.RandomAccess",
        "System.Diagnostics.Process",
    };

    /// <summary>
    /// The namespaces whose use means the Domain is touching the outside world.
    /// </summary>
    private static readonly string[] ForbiddenNamespaces =
    [
        "System.IO.Enumeration",
        "System.Net",
        "Microsoft.Win32",
    ];

    [Fact]
    internal void DomainTypes_UseNoFileSystemNetworkOrProcessApi()
    {
        var violations = typeof(BackupLayout)
            .Assembly.GetTypes()
            .Where(static type => !IsInNamespace(type, CoverageInstrumentationNamespace))
            .SelectMany(type =>
                TypeReferences
                    .Of(type)
                    .Where(IsForbidden)
                    .Select(used => $"{type.FullName} -> {used.FullName}")
            )
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// Determines whether a referenced type is an I/O API the Domain must not use.
    /// </summary>
    /// <param name="type">The referenced type.</param>
    /// <returns><see langword="true"/> when the type performs I/O.</returns>
    private static bool IsForbidden(Type type)
    {
        return ForbiddenTypes.Contains(type.FullName ?? string.Empty)
            || ForbiddenNamespaces.Any(ns => IsInNamespace(type, ns));
    }

    /// <summary>
    /// Determines whether a type is declared in a namespace or one nested inside it.
    /// </summary>
    /// <param name="type">The type to classify.</param>
    /// <param name="ns">The namespace.</param>
    /// <returns><see langword="true"/> when the type's namespace is <paramref name="ns"/> or nested inside it.</returns>
    private static bool IsInNamespace(Type type, string ns)
    {
        return type.Namespace is { } typeNamespace
            && (typeNamespace == ns || typeNamespace.StartsWith(ns + ".", StringComparison.Ordinal));
    }
}
