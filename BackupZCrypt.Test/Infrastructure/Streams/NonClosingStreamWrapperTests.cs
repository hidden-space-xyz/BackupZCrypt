namespace BackupZCrypt.Test.Infrastructure.Streams;

using BackupZCrypt.Infrastructure.Streams;

[TestFixture]
internal sealed class NonClosingStreamWrapperTests
{
    [Test]
    public void ReadAndWrite_DelegateToInner()
    {
        using MemoryStream inner = new();
        using NonClosingStreamWrapper wrapper = new(inner);

        wrapper.Write([10, 20, 30], 0, 3);
        wrapper.Position = 0;

        var buffer = new byte[3];
        var read = wrapper.Read(buffer, 0, 3);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(read, Is.EqualTo(3));
            Assert.That(buffer, Is.EqualTo(new byte[] { 10, 20, 30 }));
        }
    }

    [Test]
    public async Task ReadAsyncAndWriteAsync_DelegateToInner()
    {
        await using MemoryStream inner = new();
        await using NonClosingStreamWrapper wrapper = new(inner);

        await wrapper.WriteAsync(new byte[] { 4, 5, 6 }.AsMemory());
        wrapper.Position = 0;

        var buffer = new byte[3];
        var read = await wrapper.ReadAsync(buffer.AsMemory(0, 3));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(read, Is.EqualTo(3));
            Assert.That(buffer, Is.EqualTo(new byte[] { 4, 5, 6 }));
        }
    }

    [Test]
    public void Dispose_DoesNotCloseInnerStream()
    {
        MemoryStream inner = new([1, 2, 3]);

        NonClosingStreamWrapper wrapper = new(inner);
        wrapper.Dispose();

        Assert.That(inner.CanRead, Is.True);
        var buffer = new byte[3];
        inner.Position = 0;
        var read = inner.Read(buffer, 0, 3);
        Assert.That(read, Is.EqualTo(3));

        inner.Dispose();
    }

    [Test]
    public async Task DisposeAsync_DoesNotCloseInnerStream()
    {
        MemoryStream inner = new([1, 2, 3]);

        NonClosingStreamWrapper wrapper = new(inner);
        await wrapper.DisposeAsync();

        Assert.That(inner.CanRead, Is.True);
        inner.Position = 0;
        var buffer = new byte[3];
        var read = await inner.ReadAsync(buffer.AsMemory(0, 3));
        Assert.That(read, Is.EqualTo(3));

        await inner.DisposeAsync();
    }
}
