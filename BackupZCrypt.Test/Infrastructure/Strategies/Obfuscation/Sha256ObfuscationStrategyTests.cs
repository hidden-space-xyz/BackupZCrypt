namespace BackupZCrypt.Test.Infrastructure.Strategies.Obfuscation;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Infrastructure.Strategies.Obfuscation;

[TestFixture]
internal sealed class Sha256ObfuscationStrategyTests
{
    private Sha256ObfuscationStrategy strategy = null!;

    [SetUp]
    public void SetUp()
    {
        this.strategy = new Sha256ObfuscationStrategy();
    }

    [Test]
    public void Id_ReturnsSha256()
    {
        Assert.That(this.strategy.Id, Is.EqualTo(NameObfuscationMode.Sha256));
    }

    [Test]
    public void DisplayName_ReturnsSHA256()
    {
        Assert.That(this.strategy.DisplayName, Is.EqualTo("SHA-256"));
    }

    [Test]
    public void Description_IsNotEmpty()
    {
        Assert.That(this.strategy.Description, Is.Not.Empty);
    }

    [Test]
    public void Summary_IsNotEmpty()
    {
        Assert.That(this.strategy.Summary, Is.Not.Empty);
    }

    [Test]
    public void ObfuscateFileName_FileDoesNotExist_UsesPathHash()
    {
        var result = this.strategy.ObfuscateFileName(@"C:\nonexistent\file.txt", "file.bzc");

        Assert.That(result, Does.EndWith(".bzc"));
        var hashPart = Path.GetFileNameWithoutExtension(result);
        Assert.That(hashPart, Has.Length.EqualTo(64));
    }

    [Test]
    public void ObfuscateFileName_DeterministicForSamePath()
    {
        var result1 = this.strategy.ObfuscateFileName(@"C:\nonexistent\file.txt", "file.bzc");
        var result2 = this.strategy.ObfuscateFileName(@"C:\nonexistent\file.txt", "file.bzc");

        Assert.That(result1, Is.EqualTo(result2));
    }

    [Test]
    public void ObfuscateFileName_DifferentPathsProduceDifferentHashes()
    {
        var result1 = this.strategy.ObfuscateFileName(@"C:\path1\file.txt", "file.bzc");
        var result2 = this.strategy.ObfuscateFileName(@"C:\path2\file.txt", "file.bzc");

        Assert.That(result1, Is.Not.EqualTo(result2));
    }

    [Test]
    public void ObfuscateFileName_PreservesExtension()
    {
        var result = this.strategy.ObfuscateFileName(@"C:\nonexistent\file.txt", "file.jpg");

        Assert.That(result, Does.EndWith(".jpg"));
    }

    [Test]
    public void ObfuscateFileName_ExistingFile_UsesFileContentHash()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "test content");

            var result = this.strategy.ObfuscateFileName(tempFile, "test.bzc");

            Assert.That(result, Does.EndWith(".bzc"));
            var hashPart = Path.GetFileNameWithoutExtension(result);
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
        var tempFile1 = Path.GetTempFileName();
        var tempFile2 = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile1, "identical content");
            File.WriteAllText(tempFile2, "identical content");

            var result1 = this.strategy.ObfuscateFileName(tempFile1, "a.bzc");
            var result2 = this.strategy.ObfuscateFileName(tempFile2, "b.bzc");

            Assert.That(result1, Is.EqualTo(result2));
        }
        finally
        {
            File.Delete(tempFile1);
            File.Delete(tempFile2);
        }
    }
}
