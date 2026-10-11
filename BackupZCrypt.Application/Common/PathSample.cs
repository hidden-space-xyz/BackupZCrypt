namespace BackupZCrypt.Application.Common;

/// <summary>
/// The first few paths a message is about when it is about many files at once, carried as a message
/// argument so the presentation layer decides how to list them.
/// </summary>
public sealed class PathSample : IEquatable<PathSample>
{
    /// <summary>
    /// The number of paths named before the sample is cut short.
    /// </summary>
    private const int MaximumNamedPaths = 5;

    /// <summary>
    /// Initializes a new instance of the <see cref="PathSample"/> class.
    /// </summary>
    /// <param name="paths">The paths named, in order.</param>
    /// <param name="isTruncated">Whether more paths were left out.</param>
    private PathSample(IReadOnlyList<string> paths, bool isTruncated)
    {
        this.Paths = paths;
        this.IsTruncated = isTruncated;
    }

    /// <summary>
    /// Gets the paths named, in order; at most five.
    /// </summary>
    public IReadOnlyList<string> Paths { get; }

    /// <summary>
    /// Gets a value indicating whether more paths were left out than are named.
    /// </summary>
    public bool IsTruncated { get; }

    /// <summary>
    /// Takes the first few paths, in order, and records whether there were more.
    /// </summary>
    /// <param name="paths">The paths to sample.</param>
    /// <returns>The sample.</returns>
    internal static PathSample Of(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var sample = paths.Take(MaximumNamedPaths + 1).ToList();
        var isTruncated = sample.Count > MaximumNamedPaths;

        return new(Array.AsReadOnly([.. sample.Take(MaximumNamedPaths)]), isTruncated);
    }

    /// <inheritdoc/>
    public bool Equals(PathSample? other)
    {
        return other is not null
            && this.IsTruncated == other.IsTruncated
            && this.Paths.SequenceEqual(other.Paths, StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        return this.Equals(obj as PathSample);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(this.IsTruncated);

        foreach (var path in this.Paths)
        {
            hash.Add(path, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
