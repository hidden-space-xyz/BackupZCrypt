using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Infrastructure.Strategies.Obfuscation;

namespace CloudZCrypt.Test.Infrastructure.Strategies.Obfuscation;

[TestFixture]
internal sealed class GuidObfuscationStrategyTests
{
    private GuidObfuscationStrategy _strategy = null!;

    [SetUp]
    public void SetUp()
    {
        _strategy = new GuidObfuscationStrategy();
    }

    [Test]
    public void Id_ReturnsGuid()
    {
        Assert.That(_strategy.Id, Is.EqualTo(NameObfuscationMode.Guid));
    }

    [Test]
    public void DisplayName_ReturnsGUID()
    {
        Assert.That(_strategy.DisplayName, Is.EqualTo("GUID"));
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
    public void ObfuscateFileName_ReturnsGuidWithExtension()
    {
        string result = _strategy.ObfuscateFileName(@"C:\source\file.txt", "file.czc");

        Assert.That(result, Does.EndWith(".czc"));
        string guidPart = Path.GetFileNameWithoutExtension(result);
        Assert.That(Guid.TryParse(guidPart, out _), Is.True);
    }

    [Test]
    public void ObfuscateFileName_PreservesExtension()
    {
        string result = _strategy.ObfuscateFileName(@"C:\source\photo.jpg", "photo.jpg");

        Assert.That(result, Does.EndWith(".jpg"));
    }

    [Test]
    public void ObfuscateFileName_DifferentCallsProduceDifferentNames()
    {
        string result1 = _strategy.ObfuscateFileName(@"C:\source\file.txt", "file.czc");
        string result2 = _strategy.ObfuscateFileName(@"C:\source\file.txt", "file.czc");

        Assert.That(result1, Is.Not.EqualTo(result2));
    }

    [Test]
    public void ObfuscateFileName_NoExtension_ReturnsGuidOnly()
    {
        string result = _strategy.ObfuscateFileName(@"C:\source\noext", "noext");

        Assert.That(Guid.TryParse(result, out _), Is.True);
    }
}
