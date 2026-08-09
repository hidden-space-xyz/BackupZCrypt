using System.Security.Cryptography;
using System.Text;

using BackupZCrypt.Application.Commands;
using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

namespace BackupZCrypt.Test.Tools;

/// <summary>
/// One-shot maintenance tool that regenerates the committed fixture archives under
/// <c>TestData/OnDiskFormatArchives</c> and prints the golden key-schedule vectors asserted by
/// <c>OnDiskFormatTests</c>.
/// </summary>
/// <remarks>
/// <para>
/// Maintenance tool: rewrites the committed format fixtures. Run only on a deliberate format
/// change. Every case therefore declares <c>Explicit = true</c> on its <see cref="FactAttribute"/>
/// — xUnit has no class-level form — so a normal <c>dotnet test</c> never runs it: the fixtures
/// exist precisely to detect a change in the on-disk format, so regenerating them on each run
/// would defeat their entire purpose.
/// </para>
/// <para>
/// Run it only when the format is changed <b>deliberately</b>, and treat the resulting diff as the
/// review artefact — a fixture that changes without an intended format change is a data-loss bug:
/// <c>dotnet run --project BackupZCrypt.Test -- -explicit only -class "*OnDiskFormatFixtureGenerator"</c>.
/// </para>
/// <para>
/// That command bypasses <c>dotnet test</c> on purpose. An explicit test stays unrun however
/// narrowly a <c>--filter</c> names it, and only <c>-explicit only</c> selects it — an option the
/// VSTest runner cannot forward, so the v3 assembly's own runner is invoked directly. No filter
/// typo can therefore reach this class by accident.
/// </para>
/// </remarks>
public sealed class OnDiskFormatFixtureGenerator
{
    /// <summary>
    /// The password the fixture archives are created with, shared with <c>OnDiskFormatTests</c>.
    /// </summary>
    private const string Password = "Correct-Horse-Battery-Staple-42";

    /// <summary>
    /// Recreates every fixture archive from the same source tree the pinning tests expect.
    /// </summary>
    /// <returns>A task that completes once every fixture has been rewritten.</returns>
    [Fact(Explicit = true)]
    internal async Task RegenerateFixtureArchives()
    {
        foreach (var fixture in OnDiskFormatFixtures.All)
        {
            await RegenerateAsync(fixture);
        }
    }

    /// <summary>
    /// Prints the golden key-schedule vectors as ready-to-paste C# literals.
    /// </summary>
    [Fact(Explicit = true)]
    internal void PrintGoldenVectors()
    {
        var salt = OnDiskFormatFixtures.GoldenSalt;

        using var provider = TestHost.CreateProvider();
        var factory = provider.GetRequiredService<IKeyDerivationServiceFactory>();

        var algorithms = Enum.GetValues<KeyDerivationAlgorithm>();

        Assert.NotEmpty(algorithms);

        foreach (var kdf in algorithms)
        {
            var masterKey = factory.Create(kdf).DeriveKey(Password, salt, EncryptionConstants.KeySize);
            Assert.Equal(EncryptionConstants.KeySize / 8, masterKey.Length);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{kdf} master key: {Convert.ToHexStringLower(masterKey)}"
            );

            foreach (var label in OnDiskFormatFixtures.SubKeyLabels)
            {
                var subKey = new byte[EncryptionConstants.KeySize / 8];
                HKDF.Expand(
                    HashAlgorithmName.SHA256,
                    masterKey,
                    subKey,
                    Encoding.UTF8.GetBytes(label)
                );
                Assert.Equal(EncryptionConstants.KeySize / 8, subKey.Length);
                TestContext.Current.TestOutputHelper?.WriteLine(
                    $"  {label}: {Convert.ToHexStringLower(subKey)}"
                );
            }

            CryptographicOperations.ZeroMemory(masterKey);
        }

        var chunkHash = SHA256.HashData(OnDiskFormatFixtures.GoldenChunkContent);
        var namingKey = Convert.FromHexString(OnDiskFormatFixtures.GoldenChunkNamingKeyHex);
        var nonceKey = Convert.FromHexString(OnDiskFormatFixtures.GoldenChunkNonceKeyHex);

        Assert.Multiple(
            () => Assert.Equal(SHA256.HashSizeInBytes, chunkHash.Length),
            () =>
                Assert.True(
                    HMACSHA256.HashData(nonceKey, chunkHash).Length >= EncryptionConstants.NonceSize
                )
        );

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"chunk hash: {Convert.ToHexStringLower(chunkHash)}"
        );
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"chunk file name: {Convert.ToHexStringLower(HMACSHA256.HashData(namingKey, chunkHash))}"
        );
        TestContext.Current.TestOutputHelper?.WriteLine(
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

        await using var provider = TestHost.CreateProvider();

        var result = await provider
            .GetRequiredService<ICommandHandler<CreateBackupCommand, Result<BackupOutcome>>>()
            .HandleAsync(
                new CreateBackupCommand(
                    source.Path,
                    target,
                    Password,
                    Password,
                    fixture.Encryption,
                    fixture.KeyDerivation,
                    fixture.Compression,
                    ProceedOnWarnings: true
                )
                {
                    Progress = new RecordingProgress<BackupStatus>(),
                },
                CancellationToken.None
            );

        Assert.True(result.IsSuccess, $"Could not generate the '{fixture.Name}' fixture.");
        TestContext.Current.TestOutputHelper?.WriteLine($"Regenerated {fixture.Name} at {target}");
    }
}
