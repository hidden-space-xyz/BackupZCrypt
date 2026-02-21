namespace BackupZCrypt.Test.Domain.Constants;

using BackupZCrypt.Domain.Constants;

[TestFixture]
internal sealed class FileCryptConstantsTests
{
    [Test]
    public void AppFileExtension_IsCzc()
    {
        Assert.That(FileCryptConstants.AppFileExtension, Is.EqualTo(".czc"));
    }

    [Test]
    public void ManifestFileName_ContainsExtension()
    {
        Assert.That(FileCryptConstants.ManifestFileName, Is.EqualTo("manifest.czc"));
    }

    [Test]
    public void ManifestFileName_EndsWithAppFileExtension()
    {
        Assert.That(
            FileCryptConstants.ManifestFileName,
            Does.EndWith(FileCryptConstants.AppFileExtension));
    }
}
