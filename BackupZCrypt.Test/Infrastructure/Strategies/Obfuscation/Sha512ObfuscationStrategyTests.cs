namespace BackupZCrypt.Test.Infrastructure.Strategies.Obfuscation;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Infrastructure.Strategies.Obfuscation;

[TestFixture]
internal sealed class Sha512ObfuscationStrategyTests
{
    private Sha512ObfuscationStrategy strategy = null!;

    [SetUp]
    public void SetUp()
    {
        this.strategy = new Sha512ObfuscationStrategy();
    }

    [Test]
    public void Id_ReturnsSha512()
    {
        Assert.That(this.strategy.Id, Is.EqualTo(NameObfuscationMode.Sha512));
    }

    [Test]
    public void DisplayName_ReturnsSHA512()
    {
        Assert.That(this.strategy.DisplayName, Is.EqualTo("SHA-512"));
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
    public void ObfuscateFileName_FileDoesNotExist_Returns128CharHash()
    {
        string result = this.strategy.ObfuscateFileName(@"C:\nonexistent\file.txt", "file.bzc");

        Assert.That(result, Does.EndWith(".bzc"));
        string hashPart = Path.GetFileNameWithoutExtension(result);
        Assert.That(hashPart, Has.Length.EqualTo(128));
    }

    [Test]
    public void ObfuscateFileName_DeterministicForSamePath()
    {
        string result1 = this.strategy.ObfuscateFileName(@"C:\nonexistent\file.txt", "file.bzc");
        string result2 = this.strategy.ObfuscateFileName(@"C:\nonexistent\file.txt", "file.bzc");

        Assert.That(result1, Is.EqualTo(result2));
    }

    [Test]
    public void ObfuscateFileName_DifferentPathsProduceDifferentHashes()
    {
        string result1 = this.strategy.ObfuscateFileName(@"C:\path1\file.txt", "file.bzc");
        string result2 = this.strategy.ObfuscateFileName(@"C:\path2\file.txt", "file.bzc");

        Assert.That(result1, Is.Not.EqualTo(result2));
    }

    [Test]
    public void ObfuscateFileName_PreservesExtension()
    {
        string result = this.strategy.ObfuscateFileName(@"C:\nonexistent\file.txt", "file.dat");

        Assert.That(result, Does.EndWith(".dat"));
    }

    [Test]
    public void ObfuscateFileName_ExistingFile_UsesFileContentHash()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "test content for sha512");

            string result = this.strategy.ObfuscateFileName(tempFile, "test.bzc");

            Assert.That(result, Does.EndWith(".bzc"));
            string hashPart = Path.GetFileNameWithoutExtension(result);
            Assert.That(hashPart, Has.Length.EqualTo(128));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
