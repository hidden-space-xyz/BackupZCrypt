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
/// Unit tests for the update-backup command handler: the mapping of the command onto an update
/// request, progress forwarding, and the redaction of the password in the command's text form. The
/// handler runs over a real runner whose leaf dependencies are substituted.
/// </summary>
public sealed class UpdateBackupCommandHandlerTests
{
    /// <summary>
    /// The rooted source path the commands point at; nothing is created on disk.
    /// </summary>
    private static readonly string SourceDir = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "bzc-update-handler-src")
    );

    /// <summary>
    /// The rooted archive path the commands point at, kept distinct from <see cref="SourceDir"/>.
    /// </summary>
    private static readonly string BackupDir = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "bzc-update-handler-dst")
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
    private UpdateBackupCommandHandler CreateSut()
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

    [Test]
    public async Task HandleAsync_Command_MapsOntoAnUpdateRequestAndForwardsTheProgress()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(BackupDir).Returns(true);

        BackupRequest? captured = null;
        _ = this.chunkedBackupService
            .UpdateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Do<BackupRequest>(request => captured = request),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result<BackupResult>.Success(new BackupResult(true, TimeSpan.Zero, 0, 0, 0)));

        var command = new UpdateBackupCommand(
            SourceDir,
            BackupDir,
            "Correct-Horse-Battery-Staple-42",
            ProceedOnWarnings: true
        )
        {
            Progress = this.progress,
        };

        var result = await this.CreateSut().HandleAsync(command, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Completion!.IsSuccess, Is.True);
            Assert.That(captured!.SourcePath, Is.EqualTo(SourceDir));
            Assert.That(captured.DestinationPath, Is.EqualTo(BackupDir));
            Assert.That(captured.Password, Is.EqualTo("Correct-Horse-Battery-Staple-42"));
            Assert.That(captured.ConfirmPassword, Is.EqualTo(captured.Password));
            Assert.That(captured.Operation, Is.EqualTo(BackupOperation.Update));
            Assert.That(captured.ProceedOnWarnings, Is.True);
        }

        await this.chunkedBackupService.Received(1)
            .UpdateAsync(
                SourceDir,
                BackupDir,
                Arg.Any<BackupRequest>(),
                this.progress,
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public void ToString_OfTheCommand_RedactsThePassword()
    {
        var command = new UpdateBackupCommand(SourceDir, BackupDir, "hunter2-secret");

        var text = command.ToString();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Not.Contain("hunter2-secret"));
            Assert.That(text, Does.Contain("***"));
        }
    }
}
