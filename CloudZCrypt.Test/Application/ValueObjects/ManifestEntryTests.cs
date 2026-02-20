namespace CloudZCrypt.Test.Application.ValueObjects;

using CloudZCrypt.Application.ValueObjects.Manifest;

[TestFixture]
internal sealed class ManifestEntryTests
{
    [Test]
    public void Record_SetsProperties()
    {
        ManifestEntry entry = new("original/path.txt", "obfuscated/hash.czc");

        Assert.That(entry.OriginalRelativePath, Is.EqualTo("original/path.txt"));
        Assert.That(entry.ObfuscatedRelativePath, Is.EqualTo("obfuscated/hash.czc"));
    }

    [Test]
    public void Record_EqualityByValue()
    {
        ManifestEntry a = new("a.txt", "b.czc");
        ManifestEntry b = new("a.txt", "b.czc");

        Assert.That(a, Is.EqualTo(b));
    }
}
