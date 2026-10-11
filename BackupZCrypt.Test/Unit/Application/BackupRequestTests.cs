using BackupZCrypt.Application.Backup;
using BackupZCrypt.Domain.Algorithms;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the per-operation backup requests. A record prints every member by default, so
/// each request must keep the password, and a create also its confirmation, out of
/// <see cref="object.ToString"/>, where a log line, exception message, or debugger watch would
/// otherwise capture it.
/// </summary>
public sealed class BackupRequestTests
{
    /// <summary>
    /// The secret every request is built with, which must never appear in its text.
    /// </summary>
    private const string Secret = "hunter2-secret";

    /// <summary>
    /// Supplies one request of every type, each built with <see cref="Secret"/> as its password.
    /// </summary>
    /// <returns>The requests to format.</returns>
    public static TheoryData<object> EveryRequest()
    {
        return new TheoryData<object>
        {
            new CreateBackupRequest(
                "source",
                "archive",
                Secret,
                Secret,
                EncryptionAlgorithm.Aes,
                KeyDerivationAlgorithm.Argon2id
            ),
            new UpdateBackupRequest("source", "archive", Secret),
            new RestoreBackupRequest("archive", "restored", Secret),
            new VerifyBackupRequest("archive", Secret),
        };
    }

    [Theory]
    [MemberData(nameof(EveryRequest))]
    internal void ToString_OfEveryRequest_RedactsThePasswordButKeepsThePaths(object request)
    {
        var text = request.ToString()!;

        Assert.Multiple(
            () => Assert.DoesNotContain(Secret, text, StringComparison.Ordinal),
            () => Assert.Contains("Password = ***", text, StringComparison.Ordinal),
            () => Assert.Contains("archive", text, StringComparison.Ordinal)
        );
    }

    [Fact]
    internal void ToString_OfACreate_RedactsTheConfirmationAndKeepsTheAlgorithms()
    {
        var request = new CreateBackupRequest(
            "source",
            "archive",
            Secret,
            "another-secret",
            EncryptionAlgorithm.Serpent,
            KeyDerivationAlgorithm.Scrypt,
            CompressionMode.ZstdBest
        );

        var text = request.ToString();

        Assert.Multiple(
            () => Assert.DoesNotContain("another-secret", text, StringComparison.Ordinal),
            () => Assert.Contains("ConfirmPassword = ***", text, StringComparison.Ordinal),
            () => Assert.Contains("EncryptionAlgorithm = Serpent", text, StringComparison.Ordinal),
            () => Assert.Contains("KeyDerivationAlgorithm = Scrypt", text, StringComparison.Ordinal),
            () => Assert.Contains("Compression = ZstdBest", text, StringComparison.Ordinal)
        );
    }
}
