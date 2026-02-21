namespace BackupZCrypt.Test.Infrastructure.Strategies.Obfuscation;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Infrastructure.Strategies.Obfuscation;

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
    public void Id_ReturnsGuid()
    {
        Assert.That(this.strategy.Id, Is.EqualTo(NameObfuscationMode.Guid));
    }

    [Test]
    public void DisplayName_ReturnsGUID()
    {
        Assert.That(this.strategy.DisplayName, Is.EqualTo("GUID"));
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
    public void ObfuscateFileName_ReturnsGuidWithExtension()
    {
        string result = this.strategy.ObfuscateFileName(@"C:\source\file.txt", "file.czc");

        Assert.That(result, Does.EndWith(".czc"));
        string guidPart = Path.GetFileNameWithoutExtension(result);
        Assert.That(Guid.TryParse(guidPart, out _), Is.True);
    }

    [Test]
    public void ObfuscateFileName_PreservesExtension()
    {
        string result = this.strategy.ObfuscateFileName(@"C:\source\photo.jpg", "photo.jpg");

        Assert.That(result, Does.EndWith(".jpg"));
    }

    [Test]
    public void ObfuscateFileName_DifferentCallsProduceDifferentNames()
    {
        string result1 = this.strategy.ObfuscateFileName(@"C:\source\file.txt", "file.czc");
        string result2 = this.strategy.ObfuscateFileName(@"C:\source\file.txt", "file.czc");

        Assert.That(result1, Is.Not.EqualTo(result2));
    }

    [Test]
    public void ObfuscateFileName_NoExtension_ReturnsGuidOnly()
    {
        string result = this.strategy.ObfuscateFileName(@"C:\source\noext", "noext");

        Assert.That(Guid.TryParse(result, out _), Is.True);
    }
}
