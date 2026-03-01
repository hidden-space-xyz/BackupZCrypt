namespace BackupZCrypt.Test.Domain.Constants;

using BackupZCrypt.Domain.Constants;

[TestFixture]
internal sealed class FileCryptConstantsTests
{
    [Test]
    public void AppFileExtension_IsBzc()
    {
        Assert.That(FileCryptConstants.AppFileExtension, Is.EqualTo(".bzc"));
    }

    [Test]
    public void ManifestFileName_ContainsExtension()
    {
        Assert.That(FileCryptConstants.ManifestFileName, Is.EqualTo("manifest.bzc"));
    }

    [Test]
    public void ManifestFileName_EndsWithAppFileExtension()
    {
        Assert.That(
            FileCryptConstants.ManifestFileName,
            Does.EndWith(FileCryptConstants.AppFileExtension));
    }
}
