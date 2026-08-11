using BackupZCrypt.Application.Orchestrators;
using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Validators.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Test.Common;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the verify-backup query handler: the mapping of the query onto a read-only verify
/// request, progress forwarding, and the redaction of the password in the query's text form. The
/// handler runs over a real runner whose leaf dependencies are substituted.
/// </summary>
public sealed class VerifyBackupQueryHandlerTests
{
    /// <summary>
    /// The rooted archive path the queries point at; nothing is created on disk.
    /// </summary>
    private static readonly string BackupDir = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "bzc-verify-handler-src")
    );

    /// <summary>
    /// The substituted validator behind the real runner; verification must never consult it.
    /// </summary>
    private readonly IBackupRequestValidator validator =
        Substitute.For<IBackupRequestValidator>();

    /// <summary>
    /// The substituted file system behind the real runner the handler drives.
    /// </summary>
    private readonly IFileOperationsService fileOperations =
        Substitute.For<IFileOperationsService>();

    /// <summary>
    /// The substituted backup engine that captures the request the handler builds.
    /// </summary>
    private readonly IChunkedBackupService chunkedBackupService =
        Substitute.For<IChunkedBackupService>();

    /// <summary>
    /// The progress sink attached to the query; assertions check it reaches the engine unchanged.
    /// </summary>
    private readonly RecordingProgress<BackupStatus> progress = new();

    /// <summary>
    /// Creates a handler over a real runner wired to the substituted dependencies.
    /// </summary>
    /// <returns>The system under test.</returns>
    private VerifyBackupQueryHandler CreateSut()
    {
        return new(
            new BackupOperationRunner(this.validator, this.fileOperations, this.chunkedBackupService)
        );
    }

    [Fact]
    internal async Task HandleAsync_Query_MapsOntoAVerifyRequestWithoutConsultingTheValidator()
    {
        _ = this.fileOperations.DirectoryExists(BackupDir).Returns(true);

        BackupRequest? captured = null;
        _ = this.chunkedBackupService
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Do<BackupRequest>(request => captured = request),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result<BackupResult>.Success(new BackupResult(TimeSpan.Zero, 0, 0, 0)));

        var query = new VerifyBackupQuery(BackupDir, "Correct-Horse-Battery-Staple-42")
        {
            Progress = this.progress,
        };

        var result = await this.CreateSut().HandleAsync(query, CancellationToken.None);

        Assert.Multiple(
            () => Assert.True(result.IsSuccess),
            () => Assert.True(result.Value.Completion!.IsSuccess),
            () => Assert.Equal(BackupDir, captured!.SourcePath),
            () => Assert.Empty(captured!.DestinationPath),
            () => Assert.Equal("Correct-Horse-Battery-Staple-42", captured!.Password),
            () => Assert.Equal(BackupOperation.Verify, captured!.Operation)
        );

        await this.chunkedBackupService.Received(1)
            .VerifyAsync(
                BackupDir,
                Arg.Any<BackupRequest>(),
                this.progress,
                Arg.Any<CancellationToken>()
            );
        await this.validator.DidNotReceive()
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>());
        await this.validator.DidNotReceive()
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    internal void ToString_OfTheQuery_RedactsThePassword()
    {
        var query = new VerifyBackupQuery(BackupDir, "hunter2-secret");

        var text = query.ToString();

        Assert.Multiple(
            () => Assert.DoesNotContain("hunter2-secret", text, StringComparison.Ordinal),
            () => Assert.Contains("***", text, StringComparison.Ordinal)
        );
    }
}
