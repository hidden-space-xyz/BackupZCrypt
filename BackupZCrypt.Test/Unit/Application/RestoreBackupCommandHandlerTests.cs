using BackupZCrypt.Application.Commands;
using BackupZCrypt.Application.Orchestrators;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Validators.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;
using BackupZCrypt.Test.Common;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the restore-backup command handler: the mapping of the command onto a restore
/// request, progress forwarding, and the redaction of the password in the command's text form. The
/// handler runs over a real runner whose leaf dependencies are substituted.
/// </summary>
public sealed class RestoreBackupCommandHandlerTests
{
    /// <summary>
    /// The rooted archive path the commands point at; nothing is created on disk.
    /// </summary>
    private static readonly string BackupDir = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "bzc-restore-handler-src")
    );

    /// <summary>
    /// The rooted destination path the commands point at, kept distinct from <see cref="BackupDir"/>.
    /// </summary>
    private static readonly string DestinationDir = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "bzc-restore-handler-dst")
    );

    /// <summary>
    /// The substituted validator behind the real runner the handler drives.
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
    /// The progress sink attached to the command; assertions check it reaches the engine unchanged.
    /// </summary>
    private readonly RecordingProgress<BackupStatus> progress = new();

    /// <summary>
    /// Creates a handler over a real runner wired to the substituted dependencies.
    /// </summary>
    /// <returns>The system under test.</returns>
    private RestoreBackupCommandHandler CreateSut()
    {
        return new(
            new BackupOperationRunner(this.validator, this.fileOperations, this.chunkedBackupService)
        );
    }

    /// <summary>
    /// Makes the substituted validator report neither errors nor warnings.
    /// </summary>
    private void PassValidation()
    {
        List<LocalizableMessage> empty = [];

        _ = this.validator
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(empty);
        _ = this.validator
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(empty);
    }

    [Fact]
    internal async Task HandleAsync_Command_MapsOntoARestoreRequestAndForwardsTheProgress()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(BackupDir).Returns(true);

        BackupRequest? captured = null;
        _ = this.chunkedBackupService
            .RestoreAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Do<BackupRequest>(request => captured = request),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result<BackupResult>.Success(new BackupResult(true, TimeSpan.Zero, 0, 0, 0)));

        var command = new RestoreBackupCommand(
            BackupDir,
            DestinationDir,
            "Correct-Horse-Battery-Staple-42",
            ProceedOnWarnings: true
        )
        {
            Progress = this.progress,
        };

        var result = await this.CreateSut().HandleAsync(command, CancellationToken.None);

        Assert.Multiple(
            () => Assert.True(result.IsSuccess),
            () => Assert.True(result.Value.Completion!.IsSuccess),
            () => Assert.Equal(BackupDir, captured!.SourcePath),
            () => Assert.Equal(DestinationDir, captured!.DestinationPath),
            () => Assert.Equal("Correct-Horse-Battery-Staple-42", captured!.Password),
            () => Assert.Equal(captured!.Password, captured.ConfirmPassword),
            () => Assert.Equal(BackupOperation.Restore, captured!.Operation),
            () => Assert.True(captured!.ProceedOnWarnings)
        );

        await this.chunkedBackupService.Received(1)
            .RestoreAsync(
                BackupDir,
                DestinationDir,
                Arg.Any<BackupRequest>(),
                this.progress,
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    internal void ToString_OfTheCommand_RedactsThePassword()
    {
        var command = new RestoreBackupCommand(BackupDir, DestinationDir, "hunter2-secret");

        var text = command.ToString();

        Assert.Multiple(
            () => Assert.DoesNotContain("hunter2-secret", text, StringComparison.Ordinal),
            () => Assert.Contains("***", text, StringComparison.Ordinal)
        );
    }
}
