namespace BackupZCrypt.Test.Infrastructure.Strategies.Obfuscation;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Infrastructure.Strategies.Obfuscation;

[TestFixtureSource(nameof(Strategies))]
internal sealed class ObfuscationStrategyTests(
    INameObfuscationStrategy strategy,
    NameObfuscationMode expectedId)
{
    private static IEnumerable<TestFixtureData> Strategies()
    {
        yield return new TestFixtureData(new GuidObfuscationStrategy(), NameObfuscationMode.Guid)
            .SetArgDisplayNames("Guid");
        yield return new TestFixtureData(new Sha256ObfuscationStrategy(), NameObfuscationMode.Sha256)
            .SetArgDisplayNames("SHA-256");
        yield return new TestFixtureData(new Sha512ObfuscationStrategy(), NameObfuscationMode.Sha512)
            .SetArgDisplayNames("SHA-512");
    }

    [Test]
    public void Id_ReturnsExpected()
    {
        Assert.That(strategy.Id, Is.EqualTo(expectedId));
    }

    [Test]
    public void ObfuscateFileName_PreservesExtension()
    {
        var result = strategy.ObfuscateFileName(@"C:\source\file.txt", "file.dat");

        Assert.That(result, Does.EndWith(".dat"));
    }

    [Test]
    public void ObfuscateFileName_NoExtension_ReturnsValidName()
    {
        var result = strategy.ObfuscateFileName(@"C:\source\noext", "noext");

        Assert.That(result, Is.Not.Null.And.Not.Empty);
        Assert.That(Path.GetExtension(result), Is.Empty);
    }
}

[TestFixture]
internal sealed class GuidObfuscationStrategyTests
{
    private GuidObfuscationStrategy strategy = null!;

    [SetUp]
    public void SetUp()
    {
        this.strategy = new GuidObfuscationStrategy();
    }

    [Test]
    public void ObfuscateFileName_ReturnsValidGuid()
    {
        var result = this.strategy.ObfuscateFileName(@"C:\source\file.txt", "file.bzc");

        var guidPart = Path.GetFileNameWithoutExtension(result);
        Assert.That(Guid.TryParse(guidPart, out _), Is.True);
    }

    [Test]
    public void ObfuscateFileName_DifferentCallsProduceDifferentNames()
    {
        var result1 = this.strategy.ObfuscateFileName(@"C:\source\file.txt", "file.bzc");
        var result2 = this.strategy.ObfuscateFileName(@"C:\source\file.txt", "file.bzc");

        Assert.That(result1, Is.Not.EqualTo(result2));
    }
}

[TestFixture]
internal sealed class HashObfuscationStrategyTests
{
    [TestCase(typeof(Sha256ObfuscationStrategy), 64)]
    [TestCase(typeof(Sha512ObfuscationStrategy), 128)]
    public void ObfuscateFileName_ReturnsCorrectHashLength(Type strategyType, int expectedLength)
    {
        var strategy = (INameObfuscationStrategy)Activator.CreateInstance(strategyType)!;

        var result = strategy.ObfuscateFileName(@"C:\nonexistent\file.txt", "file.bzc");

        var hashPart = Path.GetFileNameWithoutExtension(result);
        Assert.That(hashPart, Has.Length.EqualTo(expectedLength));
    }

    [TestCase(typeof(Sha256ObfuscationStrategy))]
    [TestCase(typeof(Sha512ObfuscationStrategy))]
    public void ObfuscateFileName_DeterministicForSamePath(Type strategyType)
    {
        var strategy = (INameObfuscationStrategy)Activator.CreateInstance(strategyType)!;

        var result1 = strategy.ObfuscateFileName(@"C:\nonexistent\file.txt", "file.bzc");
        var result2 = strategy.ObfuscateFileName(@"C:\nonexistent\file.txt", "file.bzc");

        Assert.That(result1, Is.EqualTo(result2));
    }

    [TestCase(typeof(Sha256ObfuscationStrategy))]
    [TestCase(typeof(Sha512ObfuscationStrategy))]
    public void ObfuscateFileName_DifferentPathsProduceDifferentHashes(Type strategyType)
    {
        var strategy = (INameObfuscationStrategy)Activator.CreateInstance(strategyType)!;

        var result1 = strategy.ObfuscateFileName(@"C:\path1\file.txt", "file.bzc");
        var result2 = strategy.ObfuscateFileName(@"C:\path2\file.txt", "file.bzc");

        Assert.That(result1, Is.Not.EqualTo(result2));
    }

    [TestCase(typeof(Sha256ObfuscationStrategy))]
    [TestCase(typeof(Sha512ObfuscationStrategy))]
    public void ObfuscateFileName_SameFileContent_ProducesSameHash(Type strategyType)
    {
        var strategy = (INameObfuscationStrategy)Activator.CreateInstance(strategyType)!;
        var tempFile1 = Path.GetTempFileName();
        var tempFile2 = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile1, "identical content");
            File.WriteAllText(tempFile2, "identical content");

            var result1 = strategy.ObfuscateFileName(tempFile1, "a.bzc");
            var result2 = strategy.ObfuscateFileName(tempFile2, "b.bzc");

            Assert.That(result1, Is.EqualTo(result2));
        }
        finally
        {
            File.Delete(tempFile1);
            File.Delete(tempFile2);
        }
    }
}
