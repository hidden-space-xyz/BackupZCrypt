namespace BackupZCrypt.Test.Domain.Factories;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Factories;
using BackupZCrypt.Domain.Strategies.Interfaces;
using NSubstitute;

[TestFixture]
internal sealed class NameObfuscationServiceFactoryTests
{
    private NameObfuscationServiceFactory factory = null!;
    private INameObfuscationStrategy noneStrategy = null!;
    private INameObfuscationStrategy guidStrategy = null!;

    [SetUp]
    public void SetUp()
    {
        this.noneStrategy = Substitute.For<INameObfuscationStrategy>();
        this.noneStrategy.Id.Returns(NameObfuscationMode.None);

        this.guidStrategy = Substitute.For<INameObfuscationStrategy>();
        this.guidStrategy.Id.Returns(NameObfuscationMode.Guid);

        this.factory = new NameObfuscationServiceFactory([this.noneStrategy, this.guidStrategy]);
    }

    [Test]
    public void Create_RegisteredMode_ReturnsStrategy()
    {
        INameObfuscationStrategy result = this.factory.Create(NameObfuscationMode.None);

        Assert.That(result, Is.SameAs(this.noneStrategy));
    }

    [Test]
    public void Create_GuidMode_ReturnsCorrectStrategy()
    {
        INameObfuscationStrategy result = this.factory.Create(NameObfuscationMode.Guid);

        Assert.That(result, Is.SameAs(this.guidStrategy));
    }

    [Test]
    public void Create_UnregisteredMode_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            this.factory.Create(NameObfuscationMode.Sha256));
    }
}
