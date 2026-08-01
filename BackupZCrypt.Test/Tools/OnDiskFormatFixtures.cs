using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Test.Tools;

/// <summary>
/// One committed fixture archive: the algorithm combination it was written with and the directory
/// name it lives under.
/// </summary>
/// <param name="Name">The directory name under <c>TestData/LegacyArchives</c>.</param>
/// <param name="Encryption">The AEAD cipher the archive was written with.</param>
/// <param name="KeyDerivation">The key derivation function the archive was written with.</param>
/// <param name="Compression">The compression mode applied to chunks before encryption.</param>
public sealed record OnDiskFormatFixture(
    string Name,
    EncryptionAlgorithm Encryption,
    KeyDerivationAlgorithm KeyDerivation,
    CompressionMode Compression
);

/// <summary>
/// The single definition of everything the on-disk format fixtures are built from, shared by the
/// generator that writes them and the tests that pin them.
/// </summary>
/// <remarks>
/// Keeping the source tree, password, salt, and algorithm combinations in one place is what makes
/// the fixtures meaningful: the generator and the assertions cannot drift apart and quietly agree
/// on a format that changed.
/// </remarks>
public static class OnDiskFormatFixtures
{
    /// <summary>
    /// The HKDF context labels that bind each sub-key to a single purpose, in the order the backup
    /// service derives them. These strings are part of the on-disk format: changing one makes every
    /// existing archive undecryptable.
    /// </summary>
    public static readonly string[] SubKeyLabels =
    [
        "chunk-encryption",
        "chunk-nonce",
        "chunk-naming",
        "manifest-encryption",
    ];

    /// <summary>
    /// The algorithm combinations covered by a committed fixture archive: one platform-AEAD plus
    /// PBKDF2 with no compression, and one BouncyCastle cipher plus Argon2id with Zstandard.
    /// </summary>
    public static readonly OnDiskFormatFixture[] All =
    [
        new(
            "aes-pbkdf2-none",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            CompressionMode.None
        ),
        new(
            "twofish-argon2-zstd",
            EncryptionAlgorithm.Twofish,
            KeyDerivationAlgorithm.Argon2id,
            CompressionMode.Zstd
        ),
    ];

    /// <summary>
    /// The source tree every fixture archive was created from, as relative path and UTF-8 content.
    /// </summary>
    /// <remarks>
    /// It deliberately contains a nested directory (so entry-path portability is covered), a file
    /// whose bytes repeat (so compression actually engages), and two files with identical content
    /// (so chunk deduplication is exercised).
    /// </remarks>
    public static readonly (string RelativePath, string Content)[] SourceTree =
    [
        ("root.txt", "root file contents"),
        ("docs/notes.md", "# notes\nsecond line\n"),
        ("docs/sub/deep.txt", "deep file contents"),
        ("duplicate.txt", "root file contents"),
        ("repetitive.log", "the same line over and over\n"),
    ];

    /// <summary>
    /// The fixed 32-byte salt the golden key-schedule vectors are derived with, so the expected keys
    /// pin the KDF parameters rather than merely the fact that a key was produced.
    /// </summary>
    public static byte[] GoldenSalt =>
        [.. Enumerable.Range(0, 32).Select(static i => (byte)i)];

    /// <summary>
    /// The <c>chunk-naming</c> sub-key expanded from the PBKDF2 master key derived over
    /// <see cref="GoldenSalt"/>, used to pin the on-disk chunk file name.
    /// </summary>
    public const string GoldenChunkNamingKeyHex =
        "928c856c5cef08c0b6ddb3fd15a0627696eedae2b918df900b7c449ff07037c2";

    /// <summary>
    /// The <c>chunk-nonce</c> sub-key expanded from the same master key, used to pin the
    /// deterministic per-chunk AEAD nonce.
    /// </summary>
    public const string GoldenChunkNonceKeyHex =
        "a1bdc141f016ae95a7395ce392848c8d35270675263cbfb5a8ffaa61fc3ea157";

    /// <summary>
    /// The fixed chunk payload the golden chunk hash, name, and nonce are computed over.
    /// </summary>
    public static byte[] GoldenChunkContent => "BackupZCrypt golden chunk"u8.ToArray();

    /// <summary>
    /// Gets the repository root, located by walking up from the test assembly to the solution file.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">No ancestor directory contains the solution file.</exception>
    public static string RepositoryRoot => FindRepositoryRoot();

    /// <summary>
    /// Gets the committed fixture root inside the source tree, used by the generator to overwrite
    /// the archives in place.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">The repository root could not be located.</exception>
    public static string RepositoryTestDataRoot =>
        Path.Combine(RepositoryRoot, "BackupZCrypt.Test", "TestData", "LegacyArchives");

    /// <summary>
    /// Gets the fixture root copied next to the test assembly, used by the pinning tests at run time.
    /// </summary>
    public static string DeployedTestDataRoot =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "LegacyArchives");

    /// <summary>
    /// Walks up from the test assembly location until it finds the directory holding the solution file.
    /// </summary>
    /// <returns>The absolute path of the repository root.</returns>
    /// <exception cref="DirectoryNotFoundException">No ancestor directory contains the solution file.</exception>
    private static string FindRepositoryRoot()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, "BackupZCrypt.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate BackupZCrypt.sln above " + AppContext.BaseDirectory
        );
    }
}
