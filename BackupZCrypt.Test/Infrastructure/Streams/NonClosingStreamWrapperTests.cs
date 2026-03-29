namespace BackupZCrypt.Test.Infrastructure.Streams;

using BackupZCrypt.Infrastructure.Streams;

[TestFixture]
internal sealed class NonClosingStreamWrapperTests
{
    [Test]
    public void CanRead_DelegatesToInner()
    {
        using MemoryStream inner = new([1, 2, 3]);
        using NonClosingStreamWrapper wrapper = new(inner);

        Assert.That(wrapper.CanRead, Is.True);
    }

    [Test]
    public void CanWrite_DelegatesToInner()
    {
        using MemoryStream inner = new();
        using NonClosingStreamWrapper wrapper = new(inner);

        Assert.That(wrapper.CanWrite, Is.True);
    }

    [Test]
    public void CanSeek_DelegatesToInner()
    {
        using MemoryStream inner = new();
        using NonClosingStreamWrapper wrapper = new(inner);

        Assert.That(wrapper.CanSeek, Is.True);
    }

    [Test]
    public void Length_DelegatesToInner()
    {
        using MemoryStream inner = new([1, 2, 3, 4, 5]);
        using NonClosingStreamWrapper wrapper = new(inner);

        Assert.That(wrapper.Length, Is.EqualTo(5));
    }

    [Test]
    public void Position_GetSet_DelegatesToInner()
    {
        using MemoryStream inner = new([1, 2, 3, 4, 5]);
        using NonClosingStreamWrapper wrapper = new(inner);

        wrapper.Position = 3;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(wrapper.Position, Is.EqualTo(3));
            Assert.That(inner.Position, Is.EqualTo(3));
        }
    }

    [Test]
    public void Read_DelegatesToInner()
    {
        using MemoryStream inner = new([10, 20, 30]);
        using NonClosingStreamWrapper wrapper = new(inner);

        byte[] buffer = new byte[3];
        int read = wrapper.Read(buffer, 0, 3);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(read, Is.EqualTo(3));
            Assert.That(buffer, Is.EqualTo(new byte[] { 10, 20, 30 }));
        }
    }

    [Test]
    public void Write_DelegatesToInner()
    {
        using MemoryStream inner = new();
        using NonClosingStreamWrapper wrapper = new(inner);

        wrapper.Write([1, 2, 3], 0, 3);

        Assert.That(inner.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
    }

    [Test]
    public async Task ReadAsync_DelegatesToInner()
    {
        await using MemoryStream inner = new([10, 20, 30]);
        await using NonClosingStreamWrapper wrapper = new(inner);

        byte[] buffer = new byte[3];
        int read = await wrapper.ReadAsync(buffer.AsMemory(0, 3));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(read, Is.EqualTo(3));
            Assert.That(buffer, Is.EqualTo(new byte[] { 10, 20, 30 }));
        }
    }

    [Test]
    public async Task WriteAsync_DelegatesToInner()
    {
        await using MemoryStream inner = new();
        await using NonClosingStreamWrapper wrapper = new(inner);

        await wrapper.WriteAsync(new byte[] { 4, 5, 6 }.AsMemory());

        Assert.That(inner.ToArray(), Is.EqualTo(new byte[] { 4, 5, 6 }));
    }

    [Test]
    public void Seek_DelegatesToInner()
    {
        using MemoryStream inner = new([1, 2, 3, 4, 5]);
        using NonClosingStreamWrapper wrapper = new(inner);

        long position = wrapper.Seek(2, SeekOrigin.Begin);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(position, Is.EqualTo(2));
            Assert.That(inner.Position, Is.EqualTo(2));
        }
    }

    [Test]
    public void SetLength_DelegatesToInner()
    {
        using MemoryStream inner = new();
        using NonClosingStreamWrapper wrapper = new(inner);

        wrapper.SetLength(10);

        Assert.That(inner.Length, Is.EqualTo(10));
    }

    [Test]
    public void Dispose_DoesNotCloseInnerStream()
    {
        MemoryStream inner = new([1, 2, 3]);

        NonClosingStreamWrapper wrapper = new(inner);
        wrapper.Dispose();

        Assert.That(inner.CanRead, Is.True);
        byte[] buffer = new byte[3];
        inner.Position = 0;
        int read = inner.Read(buffer, 0, 3);
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
        byte[] buffer = new byte[3];
        int read = await inner.ReadAsync(buffer.AsMemory(0, 3));
        Assert.That(read, Is.EqualTo(3));

        await inner.DisposeAsync();
    }

    [Test]
    public void Flush_DelegatesToInner()
    {
        using MemoryStream inner = new();
        using NonClosingStreamWrapper wrapper = new(inner);

        Assert.DoesNotThrow(wrapper.Flush);
    }

    [Test]
    public async Task FlushAsync_DelegatesToInner()
    {
        await using MemoryStream inner = new();
        await using NonClosingStreamWrapper wrapper = new(inner);

        Assert.DoesNotThrowAsync(wrapper.FlushAsync);
    }
}
