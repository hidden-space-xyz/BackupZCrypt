namespace CloudZCrypt.Test.Application.ValueObjects;

using CloudZCrypt.Application.ValueObjects.Password;

[TestFixture]
internal sealed class PasswordCompositionTests
{
    [Test]
    public void CategoryCount_AllTrue_ReturnsFive()
    {
        PasswordComposition comp = new(true, true, true, true, true);

        Assert.That(comp.CategoryCount, Is.EqualTo(5));
    }

    [Test]
    public void CategoryCount_AllFalse_ReturnsZero()
    {
        PasswordComposition comp = new(false, false, false, false, false);

        Assert.That(comp.CategoryCount, Is.EqualTo(0));
    }

    [Test]
    public void CategoryCount_TwoTrue_ReturnsTwo()
    {
        PasswordComposition comp = new(true, false, true, false, false);

        Assert.That(comp.CategoryCount, Is.EqualTo(2));
    }
}
