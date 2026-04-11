namespace BackupZCrypt.Test.Domain.Constants;

using BackupZCrypt.Domain.Constants;

[TestFixture]
internal sealed class BackupConstantsTests
{
    [Test]
    public void AppFileExtension_IsBzc()
    {
        Assert.That(BackupConstants.AppFileExtension, Is.EqualTo(".bzc"));
    }

    [Test]
    public void ManifestFileName_ContainsExtension()
    {
        Assert.That(BackupConstants.ManifestFileName, Is.EqualTo("manifest.bzc"));
    }

    [Test]
    public void ManifestFileName_EndsWithAppFileExtension()
    {
        Assert.That(
            BackupConstants.ManifestFileName,
            Does.EndWith(BackupConstants.AppFileExtension));
    }
}
