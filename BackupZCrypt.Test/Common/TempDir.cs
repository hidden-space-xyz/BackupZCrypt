using System.Text;

namespace BackupZCrypt.Test.Common;

// A unique throwaway directory under the OS temp path, recursively deleted on
// Dispose. Use inside a `using` so backup/restore integration tests never leak
// files between runs.
public sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "bzc-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public string WriteFile(string relativePath, byte[] content)
    {
        var full = Combine(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
        return full;
    }

    public string WriteText(string relativePath, string content) =>
        WriteFile(relativePath, Encoding.UTF8.GetBytes(content));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; a locked file must not fail the test.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }
}
