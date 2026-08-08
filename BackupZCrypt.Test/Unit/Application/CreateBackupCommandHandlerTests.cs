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
/// Unit tests for the create-backup command handler: the mapping of every command field onto the
/// backup request the pipeline receives, progress forwarding, and the redaction of secrets in the
/// command's text form. The handler runs over a real runner whose leaf dependencies are substituted.
/// </summary>
public sealed class CreateBackupCommandHandlerTests
{
    /// <summary>
    /// The rooted source path the commands point at; nothing is created on disk.
    /// </summary>
    private static readonly string SourceDir = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "bzc-create-handler-src")
    );

    /// <summary>
    /// The rooted destination path the commands point at, kept distinct from <see cref="SourceDir"/>.
    /// </summary>
    private static readonly string DestinationDir = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "bzc-create-handler-dst")
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
    private CreateBackupCommandHandler CreateSut()
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
    public async Task HandleAsync_Command_MapsEveryFieldOntoACreateRequestAndForwardsTheProgress()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);

        BackupRequest? captured = null;
        _ = this.chunkedBackupService
            .CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Do<BackupRequest>(request => captured = request),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result<BackupResult>.Success(new BackupResult(true, TimeSpan.Zero, 0, 0, 0)));

        var command = new CreateBackupCommand(
            SourceDir,
            DestinationDir,
            "Correct-Horse-Battery-Staple-42",
            "Correct-Horse-Battery-Staple-42",
            EncryptionAlgorithm.Twofish,
            KeyDerivationAlgorithm.Scrypt,
            CompressionMode.ZstdBest,
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
            Assert.That(captured.DestinationPath, Is.EqualTo(DestinationDir));
            Assert.That(captured.Password, Is.EqualTo("Correct-Horse-Battery-Staple-42"));
            Assert.That(captured.ConfirmPassword, Is.EqualTo("Correct-Horse-Battery-Staple-42"));
            Assert.That(captured.EncryptionAlgorithm, Is.EqualTo(EncryptionAlgorithm.Twofish));
            Assert.That(captured.KeyDerivationAlgorithm, Is.EqualTo(KeyDerivationAlgorithm.Scrypt));
            Assert.That(captured.Operation, Is.EqualTo(BackupOperation.Create));
            Assert.That(captured.Compression, Is.EqualTo(CompressionMode.ZstdBest));
            Assert.That(captured.ProceedOnWarnings, Is.True);
        }

        await this.chunkedBackupService.Received(1)
            .CreateAsync(
                SourceDir,
                DestinationDir,
                Arg.Any<BackupRequest>(),
                this.progress,
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public void ToString_OfTheCommand_RedactsThePasswordAndConfirmation()
    {
        var command = new CreateBackupCommand(
            SourceDir,
            DestinationDir,
            "hunter2-secret",
            "hunter2-secret",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id
        );

        var text = command.ToString();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Not.Contain("hunter2-secret"));
            Assert.That(text, Does.Contain("***"));
        }
    }
}
