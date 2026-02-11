using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Infrastructure.Strategies.Obfuscation;

namespace CloudZCrypt.Test.Infrastructure.Strategies.Obfuscation;

[TestFixture]
internal sealed class Sha256ObfuscationStrategyTests
{
    private Sha256ObfuscationStrategy _strategy = null!;

    [SetUp]
    public void SetUp()
    {
        _strategy = new Sha256ObfuscationStrategy();
    }

    [Test]
    public void Id_ReturnsSha256()
    {
        Assert.That(_strategy.Id, Is.EqualTo(NameObfuscationMode.Sha256));
    }

    [Test]
    public void DisplayName_ReturnsSHA256()
    {
        Assert.That(_strategy.DisplayName, Is.EqualTo("SHA-256"));
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
    public void ObfuscateFileName_FileDoesNotExist_UsesPathHash()
    {
        string result = _strategy.ObfuscateFileName(
            @"C:\nonexistent\file.txt", "file.czc");

        Assert.That(result, Does.EndWith(".czc"));
        string hashPart = Path.GetFileNameWithoutExtension(result);
        Assert.That(hashPart, Has.Length.EqualTo(64));
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
            @"C:\nonexistent\file.txt", "file.jpg");

        Assert.That(result, Does.EndWith(".jpg"));
    }

    [Test]
    public void ObfuscateFileName_ExistingFile_UsesFileContentHash()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "test content");

            string result = _strategy.ObfuscateFileName(tempFile, "test.czc");

            Assert.That(result, Does.EndWith(".czc"));
            string hashPart = Path.GetFileNameWithoutExtension(result);
            Assert.That(hashPart, Has.Length.EqualTo(64));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public void ObfuscateFileName_SameFileContent_ProducesSameHash()
    {
        string tempFile1 = Path.GetTempFileName();
        string tempFile2 = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile1, "identical content");
            File.WriteAllText(tempFile2, "identical content");

            string result1 = _strategy.ObfuscateFileName(tempFile1, "a.czc");
            string result2 = _strategy.ObfuscateFileName(tempFile2, "b.czc");

            Assert.That(result1, Is.EqualTo(result2));
        }
        finally
        {
            File.Delete(tempFile1);
            File.Delete(tempFile2);
        }
    }
}
