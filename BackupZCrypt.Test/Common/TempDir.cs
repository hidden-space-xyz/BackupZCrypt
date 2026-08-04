using System.Text;

namespace BackupZCrypt.Test.Common;

/// <summary>
/// Disposable temporary directory, created on construction and recursively deleted on dispose.
/// </summary>
public sealed class TempDir : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TempDir"/> class, creating a uniquely named
    /// directory under the operating system temp path so parallel tests never collide.
    /// </summary>
    public TempDir()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "bzc-tests",
            Guid.NewGuid().ToString("N")
        );
        _ = Directory.CreateDirectory(Path);
    }

    /// <summary>
    /// Gets the absolute path of the temporary directory owned by this instance.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Joins the supplied segments onto the temporary directory root.
    /// </summary>
    /// <param name="parts">The ordered path segments to append to the root.</param>
    /// <returns>The combined absolute path.</returns>
    public string Combine(params string[] parts)
    {
        return System.IO.Path.Combine([Path, .. parts]);
    }

    /// <summary>
    /// Writes the supplied bytes to a path inside the temporary directory, creating any missing
    /// parent directories first.
    /// </summary>
    /// <param name="relativePath">The path of the file relative to the temporary directory root.</param>
    /// <param name="content">The bytes to write.</param>
    /// <returns>The absolute path of the file that was written.</returns>
    public string WriteFile(string relativePath, byte[] content)
    {
        var full = Combine(relativePath);
        _ = Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
        return full;
    }

    /// <summary>
    /// Writes UTF-8 encoded text to a path inside the temporary directory.
    /// </summary>
    /// <param name="relativePath">The path of the file relative to the temporary directory root.</param>
    /// <param name="content">The text to encode as UTF-8 and write.</param>
    /// <returns>The absolute path of the file that was written.</returns>
    public string WriteText(string relativePath, string content)
    {
        return WriteFile(relativePath, Encoding.UTF8.GetBytes(content));
    }

    /// <summary>
    /// Recursively deletes the temporary directory and everything under it.
    /// </summary>
    /// <remarks>
    /// Cleanup is best effort: a file still locked by the operating system, an antivirus scanner, or
    /// a leaked handle raises an I/O or access error that is reported to the test output and then
    /// swallowed, so temp-file noise can never fail an otherwise passing test. The directory then
    /// lingers until the platform reclaims it.
    /// </remarks>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TestContext.Out.WriteLine($"TempDir cleanup failed for '{Path}': {ex.Message}");
        }
    }
}
