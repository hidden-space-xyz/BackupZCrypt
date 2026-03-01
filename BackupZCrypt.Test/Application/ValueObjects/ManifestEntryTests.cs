namespace BackupZCrypt.Test.Application.ValueObjects;

using BackupZCrypt.Application.ValueObjects.Manifest;

[TestFixture]
internal sealed class ManifestEntryTests
{
    [Test]
    public void Record_SetsProperties()
    {
        ManifestEntry entry = new("original/path.txt", "obfuscated/hash.bzc");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.OriginalRelativePath, Is.EqualTo("original/path.txt"));
            Assert.That(entry.ObfuscatedRelativePath, Is.EqualTo("obfuscated/hash.bzc"));
        }
    }

    [Test]
    public void Record_EqualityByValue()
    {
        ManifestEntry a = new("a.txt", "b.bzc");
        ManifestEntry b = new("a.txt", "b.bzc");

        Assert.That(a, Is.EqualTo(b));
    }
}
