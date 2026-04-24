namespace BackupZCrypt.Test.Application.ValueObjects;

using BackupZCrypt.Application.ValueObjects;

[TestFixture]
internal sealed class ResultTests
{
    [Test]
    public void Failure_SetsIsSuccessToFalse()
    {
        var result = Result.Failure("error message");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Errors, Has.Length.EqualTo(1));
        }
        Assert.That(result.Errors[0], Is.EqualTo("error message"));
    }

    [Test]
    public void Failure_MultipleErrors_SetsAllErrors()
    {
        var result = Result.Failure("err1", "err2", "err3");

        Assert.That(result.Errors, Has.Length.EqualTo(3));
    }

    [Test]
    public void ImplicitConversion_FromString_CreatesFailure()
    {
        Result result = "some error";

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Errors[0], Is.EqualTo("some error"));
        }
    }

    [Test]
    public void GenericResult_Success_SetsValueAndIsSuccess()
    {
        var result = Result<int>.Success(42);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo(42));
            Assert.That(result.Errors, Is.Empty);
        }
    }

    [Test]
    public void GenericResult_Failure_ThrowsOnValueAccess()
    {
        var result = Result<int>.Failure("error");

        Assert.That(result.IsSuccess, Is.False);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Test]
    public void GenericResult_ExplicitSuccess_CreatesSuccess()
    {
        var result = Result<string>.Success("hello");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo("hello"));
        }
    }

    [Test]
    public void GenericResult_Failure_ContainsErrors()
    {
        var result = Result<int>.Failure("e1", "e2");

        Assert.That(result.Errors, Has.Length.EqualTo(2));
    }
}
