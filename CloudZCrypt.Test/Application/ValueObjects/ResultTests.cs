namespace CloudZCrypt.Test.Application.ValueObjects;

using CloudZCrypt.Application.ValueObjects;

[TestFixture]
internal sealed class ResultTests
{
    [Test]
    public void Failure_SetsIsSuccessToFalse()
    {
        Result result = Result.Failure("error message");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Errors, Has.Length.EqualTo(1));
        Assert.That(result.Errors[0], Is.EqualTo("error message"));
    }

    [Test]
    public void Failure_MultipleErrors_SetsAllErrors()
    {
        Result result = Result.Failure("err1", "err2", "err3");

        Assert.That(result.Errors, Has.Length.EqualTo(3));
    }

    [Test]
    public void ImplicitConversion_FromString_CreatesFailure()
    {
        Result result = "some error";

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Errors[0], Is.EqualTo("some error"));
    }

    [Test]
    public void GenericResult_Success_SetsValueAndIsSuccess()
    {
        Result<int> result = Result<int>.Success(42);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo(42));
        Assert.That(result.Errors, Is.Empty);
    }

    [Test]
    public void GenericResult_Failure_ThrowsOnValueAccess()
    {
        Result<int> result = Result<int>.Failure("error");

        Assert.That(result.IsSuccess, Is.False);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Test]
    public void GenericResult_ExplicitSuccess_CreatesSuccess()
    {
        Result<string> result = Result<string>.Success("hello");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo("hello"));
    }

    [Test]
    public void GenericResult_Failure_ContainsErrors()
    {
        Result<int> result = Result<int>.Failure("e1", "e2");

        Assert.That(result.Errors, Has.Length.EqualTo(2));
    }
}
