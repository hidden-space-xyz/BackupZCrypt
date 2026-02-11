using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Infrastructure.Strategies.Obfuscation;

namespace CloudZCrypt.Test.Infrastructure.Strategies.Obfuscation;

[TestFixture]
internal sealed class Sha512ObfuscationStrategyTests
{
    private Sha512ObfuscationStrategy _strategy = null!;

    [SetUp]
    public void SetUp()
    {
        _strategy = new Sha512ObfuscationStrategy();
    }

    [Test]
    public void Id_ReturnsSha512()
    {
        Assert.That(_strategy.Id, Is.EqualTo(NameObfuscationMode.Sha512));
    }

    [Test]
    public void DisplayName_ReturnsSHA512()
    {
        Assert.That(_strategy.DisplayName, Is.EqualTo("SHA-512"));
    }

    [Test]
    public void Description_IsNotEmpty()
    {
        Assert.That(_strategy.Description, Is.Not.Empty);
    }

    [Test]
    public void Summary_IsNotEmpty()
    {
        Assert.That(_strategy.Summary, Is.Not.Empty);
    }

    [Test]
    public void ObfuscateFileName_FileDoesNotExist_Returns128CharHash()
    {
        string result = _strategy.ObfuscateFileName(
            @"C:\nonexistent\file.txt", "file.czc");

        Assert.That(result, Does.EndWith(".czc"));
        string hashPart = Path.GetFileNameWithoutExtension(result);
        Assert.That(hashPart, Has.Length.EqualTo(128));
    }

    [Test]
    public void ObfuscateFileName_DeterministicForSamePath()
    {
        string result1 = _strategy.ObfuscateFileName(
            @"C:\nonexistent\file.txt", "file.czc");
        string result2 = _strategy.ObfuscateFileName(
            @"C:\nonexistent\file.txt", "file.czc");

        Assert.That(result1, Is.EqualTo(result2));
    }

    [Test]
    public void ObfuscateFileName_DifferentPathsProduceDifferentHashes()
    {
        string result1 = _strategy.ObfuscateFileName(
            @"C:\path1\file.txt", "file.czc");
        string result2 = _strategy.ObfuscateFileName(
            @"C:\path2\file.txt", "file.czc");

        Assert.That(result1, Is.Not.EqualTo(result2));
    }

    [Test]
    public void ObfuscateFileName_PreservesExtension()
    {
        string result = _strategy.ObfuscateFileName(
            @"C:\nonexistent\file.txt", "file.dat");

        Assert.That(result, Does.EndWith(".dat"));
    }

    [Test]
    public void ObfuscateFileName_ExistingFile_UsesFileContentHash()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "test content for sha512");

            string result = _strategy.ObfuscateFileName(tempFile, "test.czc");

            Assert.That(result, Does.EndWith(".czc"));
            string hashPart = Path.GetFileNameWithoutExtension(result);
            Assert.That(hashPart, Has.Length.EqualTo(128));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
