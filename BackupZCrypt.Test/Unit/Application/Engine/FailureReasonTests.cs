using System.Security.Cryptography;

using BackupZCrypt.Application.Common;
using BackupZCrypt.Application.Engine;

namespace BackupZCrypt.Test.Unit.Application.Engine;

/// <summary>
/// Unit tests for <see cref="FailureReason"/>, which turns the exception behind a skipped file into
/// the language-neutral reason shown next to its path.
/// </summary>
/// <remarks>
/// The sharing, lock, and disk-full cases are pinned by their HRESULT rather than by provoking the
/// failure, so the mapping is checked identically on every platform.
/// </remarks>
public sealed class FailureReasonTests
{
    /// <summary>
    /// Supplies one exception per arm of the mapping together with the reason it must produce.
    /// </summary>
    /// <returns>The exception and the expected reason code.</returns>
    public static TheoryData<Exception, MessageCode> MappedExceptions()
    {
        return new()
        {
            { new MissingChunkException(), MessageCode.ReasonChunkMissing },
            { new MissingChunkException("chunk gone"), MessageCode.ReasonChunkMissing },
            { new MissingChunkException("chunk gone", new IOException("inner")), MessageCode.ReasonChunkMissing },
            { new UnauthorizedAccessException("denied"), MessageCode.ReasonAccessDenied },
            { new FileNotFoundException("missing"), MessageCode.ReasonFileNotFound },
            { new DirectoryNotFoundException("missing"), MessageCode.ReasonFileNotFound },
            { new PathTooLongException("long"), MessageCode.ReasonPathTooLong },
            { new CryptographicException("tag"), MessageCode.ReasonDataCorrupted },
            { new InvalidDataException("size"), MessageCode.ReasonDataCorrupted },
            { new IOException("sharing", unchecked((int)0x80070020)), MessageCode.ReasonFileInUse },
            { new IOException("lock", unchecked((int)0x80070021)), MessageCode.ReasonFileInUse },
            { new IOException("full", unchecked((int)0x80070070)), MessageCode.ReasonDiskFull },
            { new IOException("end of disk", unchecked((int)0x80070027)), MessageCode.ReasonDiskFull },
            { new IOException("no space left on device", 28), MessageCode.ReasonDiskFull },
            { new IOException("device error", 5), MessageCode.ReasonIoErrorFormat },
            { new InvalidOperationException("unexpected"), MessageCode.ReasonIoErrorFormat },
        };
    }

    [Theory]
    [MemberData(nameof(MappedExceptions))]
    internal void From_KnownFailure_MapsToItsReason(Exception exception, MessageCode expected)
    {
        Assert.Equal(expected, FailureReason.From(exception).Code);
    }

    [Fact]
    internal void From_UnrecognizedIoError_CarriesTheSystemMessageAsItsOnlyArgument()
    {
        var reason = FailureReason.From(new IOException("the device is not ready"));

        Assert.Multiple(
            () => Assert.Equal(MessageCode.ReasonIoErrorFormat, reason.Code),
            () => Assert.Equal<object>(["the device is not ready"], reason.Args)
        );
    }

    [Fact]
    internal void From_RecognizedFailure_CarriesNoSystemMessage()
    {
        var reason = FailureReason.From(new UnauthorizedAccessException("Access to C:\\secret is denied."));

        Assert.Empty(reason.Args);
    }

    [Fact]
    internal void From_NullException_Throws()
    {
        _ = Assert.Throws<ArgumentNullException>(() => FailureReason.From(null!));
    }
}
