namespace BackupZCrypt.Test.Application.ValueObjects;

using BackupZCrypt.Application.ValueObjects.Manifest;

[TestFixture]
internal sealed class ManifestEntryTests
{
    [Test]
    public void Record_SetsProperties()
    {
        ManifestEntry entry = new(
            "obfuscated/hash.bzc",
            "original/path.txt",
            "c2FsdA==",
            "bm9uY2U=");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.RelativePath, Is.EqualTo("obfuscated/hash.bzc"));
            Assert.That(entry.OriginalRelativePath, Is.EqualTo("original/path.txt"));
            Assert.That(entry.Salt, Is.EqualTo("c2FsdA=="));
            Assert.That(entry.Nonce, Is.EqualTo("bm9uY2U="));
        }
    }

    [Test]
    public void Record_EqualityByValue()
    {
        ManifestEntry a = new("b.bzc", "a.txt", "c2FsdA==", "bm9uY2U=");
        ManifestEntry b = new("b.bzc", "a.txt", "c2FsdA==", "bm9uY2U=");

        Assert.That(a, Is.EqualTo(b));
    }
}
