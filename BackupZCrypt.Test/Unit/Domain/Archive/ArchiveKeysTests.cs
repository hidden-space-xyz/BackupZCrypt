using System.Text;

using BackupZCrypt.Domain.Algorithms;
using BackupZCrypt.Domain.Archive;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Domain.Archive;

/// <summary>
/// Unit tests for <see cref="ArchiveKeys"/>: every purpose gets its own sub-key, the same characters typed in
/// any Unicode composition open the same archive, and no key material is left in memory after disposal or
/// after a failed expansion.
/// </summary>
public sealed class ArchiveKeysTests
{
    /// <summary>
    /// A salt of the size archives use.
    /// </summary>
    private static readonly byte[] Salt = new byte[EncryptionConstants.SaltSize];

    [Fact]
    internal void Derive_ExpandsADistinctSubKeyForEveryPurpose()
    {
        using var sut = ArchiveKeys.Derive("password", Salt, Strategy(out _));

        byte[][] keys = [sut.MasterKey, sut.ChunkEncryptionKey, sut.ChunkNonceKey, sut.NamingKey, sut.ManifestEncryptionKey];

        Assert.Multiple(
            () => Assert.All(keys[1..], static key => Assert.Equal(EncryptionConstants.KeySize / 8, key.Length)),
            () => Assert.Equal(keys.Length, keys.Select(Convert.ToHexString).Distinct().Count())
        );
    }

    [Fact]
    internal void Derive_ComposedAndDecomposedPasswords_DeriveFromTheSameComposedForm()
    {
        var strategy = Strategy(out var passwords);

        using var composed = ArchiveKeys.Derive("café", Salt, strategy);
        using var decomposed = ArchiveKeys.Derive("café", Salt, strategy);

        Assert.Multiple(
            () => Assert.Equal(["café", "café"], passwords),
            () => Assert.Equal(composed.ChunkEncryptionKey, decomposed.ChunkEncryptionKey)
        );
    }

    [Fact]
    internal void Derive_PasswordThatIsNotValidUnicode_IsUsedAsTyped()
    {
        var strategy = Strategy(out var passwords);

        using var sut = ArchiveKeys.Derive("broken\uD800", Salt, strategy);

        Assert.Equal(["broken\uD800"], passwords);
    }

    [Fact]
    internal void Derive_NullArguments_Throw()
    {
        var strategy = Strategy(out _);

        Assert.Multiple(
            () => Assert.Throws<ArgumentNullException>(() => ArchiveKeys.Derive(null!, Salt, strategy)),
            () => Assert.Throws<ArgumentNullException>(() => ArchiveKeys.Derive("p", null!, strategy)),
            () => Assert.Throws<ArgumentNullException>(() => ArchiveKeys.Derive("p", Salt, null!)),
            () => Assert.Throws<ArgumentNullException>(static () => ArchiveKeys.FromMasterKey(null!))
        );
    }

    [Fact]
    internal void Dispose_WipesTheMasterKeyAndEverySubKey()
    {
        var sut = ArchiveKeys.Derive("password", Salt, Strategy(out _));

        sut.Dispose();

        Assert.All(
            [sut.MasterKey, sut.ChunkEncryptionKey, sut.ChunkNonceKey, sut.NamingKey, sut.ManifestEncryptionKey],
            static key => Assert.All(key, static b => Assert.Equal(0, b))
        );
    }

    [Fact]
    internal void FromMasterKey_TooShortToExpand_ThrowsAndWipesTheMasterKey()
    {
        var masterKey = Enumerable.Repeat((byte)0xAB, 8).ToArray();

        _ = Assert.ThrowsAny<ArgumentException>(() => ArchiveKeys.FromMasterKey(masterKey));

        Assert.All(masterKey, static b => Assert.Equal(0, b));
    }

    [Fact]
    internal void GenerateMasterSalt_IsFreshAndOfTheArchiveSaltSize()
    {
        var first = ArchiveKeys.GenerateMasterSalt();
        var second = ArchiveKeys.GenerateMasterSalt();

        Assert.Multiple(
            () => Assert.Equal(EncryptionConstants.SaltSize, first.Length),
            () => Assert.NotEqual(first, second)
        );
    }

    /// <summary>
    /// Builds a key-derivation strategy that derives a deterministic key from the password and records it.
    /// </summary>
    /// <param name="passwords">The passwords the strategy was asked to derive from.</param>
    /// <returns>The strategy.</returns>
    private static IKeyDerivationAlgorithmStrategy Strategy(out List<string> passwords)
    {
        List<string> received = [];
        var strategy = Substitute.For<IKeyDerivationAlgorithmStrategy>();
        _ = strategy
            .DeriveKey(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<int>())
            .Returns(call =>
            {
                var password = call.ArgAt<string>(0);
                received.Add(password);
                var key = new byte[call.ArgAt<int>(2)];
                var seed = Encoding.Unicode.GetBytes(password);
                for (var i = 0; i < key.Length; i++)
                {
                    key[i] = (byte)(seed[i % seed.Length] + i);
                }

                return key;
            });

        passwords = received;
        return strategy;
    }
}
