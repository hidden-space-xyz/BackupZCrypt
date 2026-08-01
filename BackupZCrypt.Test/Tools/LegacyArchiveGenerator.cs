using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Tools;

/// <summary>
/// One-shot maintenance tool that regenerates the committed fixture archives under
/// <c>TestData/LegacyArchives</c> and prints the golden key-schedule vectors asserted by
/// <c>OnDiskFormatTests</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every case is <see cref="ExplicitAttribute"/> so a normal <c>dotnet test</c> never runs it: the
/// fixtures exist precisely to detect a change in the on-disk format, so regenerating them on each
/// run would defeat their entire purpose.
/// </para>
/// <para>
/// Run it only when the format is changed <b>deliberately</b>, and treat the resulting diff as the
/// review artefact — a fixture that changes without an intended format change is a data-loss bug:
/// <c>dotnet test --filter "FullyQualifiedName~LegacyArchiveGenerator"</c>.
/// </para>
/// </remarks>
[Explicit("Maintenance tool: rewrites the committed format fixtures. Run only on a deliberate format change.")]
public sealed class LegacyArchiveGenerator
{
    /// <summary>
    /// The password the fixture archives are created with, shared with <c>OnDiskFormatTests</c>.
    /// </summary>
    private const string Password = "Correct-Horse-Battery-Staple-42";

    /// <summary>
    /// Recreates every fixture archive from the same source tree the pinning tests expect.
    /// </summary>
    /// <returns>A task that completes once every fixture has been rewritten.</returns>
    [Test]
    public async Task RegenerateFixtureArchives()
    {
        foreach (var fixture in OnDiskFormatFixtures.All)
        {
            await RegenerateAsync(fixture);
        }
    }

    /// <summary>
    /// Prints the golden key-schedule vectors as ready-to-paste C# literals.
    /// </summary>
    [Test]
    public void PrintGoldenVectors()
    {
        var salt = OnDiskFormatFixtures.GoldenSalt;

        using var provider = TestHost.CreateProvider();
        var factory = provider.GetRequiredService<IKeyDerivationServiceFactory>();

        foreach (var kdf in Enum.GetValues<KeyDerivationAlgorithm>())
        {
            var masterKey = factory.Create(kdf).DeriveKey(Password, salt, EncryptionConstants.KeySize);
            TestContext.Out.WriteLine($"{kdf} master key: {Convert.ToHexStringLower(masterKey)}");

            foreach (var label in OnDiskFormatFixtures.SubKeyLabels)
            {
                var subKey = new byte[EncryptionConstants.KeySize / 8];
                HKDF.Expand(
                    HashAlgorithmName.SHA256,
                    masterKey,
                    subKey,
                    Encoding.UTF8.GetBytes(label)
                );
                TestContext.Out.WriteLine($"  {label}: {Convert.ToHexStringLower(subKey)}");
            }

            CryptographicOperations.ZeroMemory(masterKey);
        }

        var chunkHash = SHA256.HashData(OnDiskFormatFixtures.GoldenChunkContent);
        var namingKey = Convert.FromHexString(OnDiskFormatFixtures.GoldenChunkNamingKeyHex);
        var nonceKey = Convert.FromHexString(OnDiskFormatFixtures.GoldenChunkNonceKeyHex);

        TestContext.Out.WriteLine($"chunk hash: {Convert.ToHexStringLower(chunkHash)}");
        TestContext.Out.WriteLine(
            $"chunk file name: {Convert.ToHexStringLower(HMACSHA256.HashData(namingKey, chunkHash))}"
        );
        TestContext.Out.WriteLine(
            "chunk nonce: "
                + Convert.ToHexStringLower(
                    HMACSHA256.HashData(nonceKey, chunkHash).AsSpan(0, EncryptionConstants.NonceSize)
                )
        );
    }

    /// <summary>
    /// Rebuilds one fixture archive in place, replacing whatever is currently committed.
    /// </summary>
    /// <param name="fixture">The fixture describing the algorithms and target directory.</param>
    /// <returns>A task that completes once the archive has been written.</returns>
    private static async Task RegenerateAsync(OnDiskFormatFixture fixture)
    {
        var target = Path.Combine(OnDiskFormatFixtures.RepositoryTestDataRoot, fixture.Name);

        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        _ = Directory.CreateDirectory(target);

        using var source = new TempDir();
        foreach (var (relativePath, content) in OnDiskFormatFixtures.SourceTree)
        {
            _ = source.WriteText(relativePath, content);
        }

        using var provider = TestHost.CreateProvider();

        var result = await provider
            .GetRequiredService<IBackupOrchestrator>()
            .ExecuteAsync(
                new BackupRequest(
                    source.Path,
                    target,
                    Password,
                    Password,
                    fixture.Encryption,
                    fixture.KeyDerivation,
                    BackupOperation.Create,
                    fixture.Compression,
                    ProceedOnWarnings: true
                ),
                new RecordingProgress<BackupStatus>(),
                CancellationToken.None
            );

        Assert.That(result.IsSuccess, Is.True, $"Could not generate the '{fixture.Name}' fixture.");
        TestContext.Out.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"Regenerated {fixture.Name} at {target}"
            )
        );
    }
}
