namespace CloudZCrypt.Test.Infrastructure.Strategies.Obfuscation;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Infrastructure.Strategies.Obfuscation;

[TestFixture]
internal sealed class NoObfuscationStrategyTests
{
    private NoObfuscationStrategy strategy = null!;

    [SetUp]
    public void SetUp()
    {
        this.strategy = new NoObfuscationStrategy();
    }

    [Test]
    public void Id_ReturnsNone()
    {
        Assert.That(this.strategy.Id, Is.EqualTo(NameObfuscationMode.None));
    }

    [Test]
    public void DisplayName_ReturnsNone()
    {
        Assert.That(this.strategy.DisplayName, Is.EqualTo("None"));
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
    public void ObfuscateFileName_ReturnsOriginalName()
    {
        string result = this.strategy.ObfuscateFileName(@"C:\source\file.txt", "file.txt");

        Assert.That(result, Is.EqualTo("file.txt"));
    }

    [Test]
    public void ObfuscateFileName_WithCzcExtension_ReturnsOriginalName()
    {
        string result = this.strategy.ObfuscateFileName(@"C:\source\file.txt", "file.txt.czc");

        Assert.That(result, Is.EqualTo("file.txt.czc"));
    }
}
