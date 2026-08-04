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
using NSubstitute.ExceptionExtensions;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the backup orchestrator: the validation gate, the post-validation source and
/// destination checks, destination preparation (including the destructive clean a create performs),
/// operation dispatch, cancellation, and unexpected-error mapping. The validator, file system, and
/// backup service are all substituted, so nothing here touches the real disk.
/// </summary>
/// <remarks>
/// A create over an existing destination cleans that directory first, which is the one irreversible act
/// the orchestrator performs: a request the user never accepted — one blocked by validation errors, by
/// warnings they declined, or aimed at a path that failed to normalize — must never reach the clean and
/// wipe a directory that already holds their data. The assertions that a clean was never received are
/// guarding exactly that, so none of them is redundant with the positive case beside it.
/// </remarks>
public sealed class BackupOrchestratorTests
{
    /// <summary>
    /// The rooted source path the requests point at. Nothing is created on disk because the file
    /// system is substituted, but the path must be absolute to survive path normalization.
    /// </summary>
    private static readonly string SourceDir = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "bzc-orchestrator-src")
    );

    /// <summary>
    /// The rooted destination path the requests point at, kept distinct from <see cref="SourceDir"/>.
    /// </summary>
    private static readonly string DestinationDir = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "bzc-orchestrator-dst")
    );

    /// <summary>
    /// The substituted validator, which lets a test choose exactly which blocking errors and advisory
    /// warnings the orchestrator sees without depending on the real validator's file-system probes.
    /// </summary>
    private readonly IBackupRequestValidator validator =
        Substitute.For<IBackupRequestValidator>();

    /// <summary>
    /// The substituted file system the orchestrator probes for the source and destination, and asks
    /// to clean and create the destination directory.
    /// </summary>
    private readonly IFileOperationsService fileOperations =
        Substitute.For<IFileOperationsService>();

    /// <summary>
    /// The substituted backup engine the orchestrator dispatches each operation to.
    /// </summary>
    private readonly IChunkedBackupService chunkedBackupService =
        Substitute.For<IChunkedBackupService>();

    /// <summary>
    /// The progress sink handed to the orchestrator; assertions check it is forwarded unchanged.
    /// </summary>
    private readonly RecordingProgress<BackupStatus> progress = new();

    /// <summary>
    /// Creates an orchestrator wired to the substituted validator, file system, and backup service.
    /// </summary>
    /// <returns>The system under test.</returns>
    private BackupOrchestrator CreateSut()
    {
        return new(this.validator, this.fileOperations, this.chunkedBackupService);
    }

    /// <summary>
    /// Builds a request whose fields are individually valid so a test only varies what it exercises.
    /// </summary>
    /// <param name="operation">The operation the request asks for.</param>
    /// <param name="proceedOnWarnings">Whether the user agreed to proceed past advisory warnings.</param>
    /// <param name="source">The source path; defaults to <see cref="SourceDir"/>.</param>
    /// <param name="destination">The destination path; defaults to <see cref="DestinationDir"/>.</param>
    /// <param name="password">The password, also used as the confirmation so the two always match.</param>
    /// <returns>A request built from the supplied values.</returns>
    private static BackupRequest Request(
        BackupOperation operation,
        bool proceedOnWarnings = false,
        string? source = null,
        string? destination = null,
        string password = "Correct-Horse-Battery-Staple-42"
    )
    {
        return new(
            source ?? SourceDir,
            destination ?? DestinationDir,
            password,
            password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.PBKDF2,
            operation,
            CompressionMode.None,
            proceedOnWarnings
        );
    }

    /// <summary>
    /// Appends a redundant <c>.</c> segment so the raw path differs from its normalized form, letting
    /// a test prove the orchestrator hands the resolved path to the backup service.
    /// </summary>
    /// <param name="path">The already-normalized path to perturb.</param>
    /// <returns>A path that normalizes back to <paramref name="path"/>.</returns>
    private static string Unnormalized(string path)
    {
        return path + Path.DirectorySeparatorChar + ".";
    }

    /// <summary>
    /// Builds a path the running platform cannot resolve to an absolute form: one longer than
    /// Windows can address, and one carrying an embedded null character everywhere else.
    /// </summary>
    /// <returns>A raw path that fails normalization.</returns>
    private static string UnnormalizablePath()
    {
        return OperatingSystem.IsWindows() ? new string('a', 300_000) : "some-folder\0name";
    }

    /// <summary>
    /// Projects messages down to their codes so assertions ignore format arguments.
    /// </summary>
    /// <param name="messages">The errors or warnings to project.</param>
    /// <returns>The code of each message, in the order reported.</returns>
    private static List<MessageCode> Codes(IReadOnlyList<LocalizableMessage> messages)
    {
        return [.. messages.Select(m => m.Code)];
    }

    /// <summary>
    /// Builds the successful outcome the substituted backup service reports.
    /// </summary>
    /// <returns>A successful result carrying a successful <see cref="BackupResult"/>.</returns>
    private static Result<BackupResult> SuccessResult()
    {
        return Result<BackupResult>.Success(new BackupResult(true, TimeSpan.Zero, 0, 0, 0));
    }

    /// <summary>
    /// Builds the validator's return shape from bare codes, since these tests only assert on codes.
    /// </summary>
    /// <param name="codes">The codes the substituted validator should report.</param>
    /// <returns>One message per code, in the order given.</returns>
    private static List<LocalizableMessage> Messages(params MessageCode[] codes)
    {
        return [.. codes.Select(code => new LocalizableMessage(code))];
    }

    /// <summary>
    /// Makes the substituted validator report neither errors nor warnings.
    /// </summary>
    private void PassValidation()
    {
        _ = this.validator
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(Messages());
        _ = this.validator
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(Messages());
    }

    /// <summary>
    /// Makes every backup service operation report success, so a test can assert on which one ran
    /// rather than on what it returned.
    /// </summary>
    private void StubOperationsSucceed()
    {
        _ = this.chunkedBackupService
            .CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(SuccessResult());
        _ = this.chunkedBackupService
            .UpdateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(SuccessResult());
        _ = this.chunkedBackupService
            .RestoreAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(SuccessResult());
    }

    [Test]
    public async Task ExecuteAsync_ValidationErrors_ReportsThemAndNeverStartsTheBackup()
    {
        _ = this.validator
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(Messages(MessageCode.PasswordTooShort));
        _ = this.fileOperations.DirectoryExists(Arg.Any<string>()).Returns(true);

        var result = await this.CreateSut()
            .ExecuteAsync(Request(BackupOperation.Create), this.progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.IsSuccess, Is.False);
            Assert.That(
                Codes(result.Value.Errors),
                Is.EqualTo([MessageCode.PasswordTooShort])
            );
            Assert.That(result.Value.Warnings, Is.Empty);
        }

        await this.validator.DidNotReceive()
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>());
        await this.fileOperations.DidNotReceive()
            .CleanDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await this.fileOperations.DidNotReceive()
            .CreateDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await this.chunkedBackupService.DidNotReceive()
            .CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ExecuteAsync_WarningsRaised_RunsOnlyWhenTheUserAgreedToProceed(
        bool proceedOnWarnings
    )
    {
        _ = this.validator
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(Messages());
        _ = this.validator
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(Messages(MessageCode.DestinationExistingFilesFormat));
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        this.StubOperationsSucceed();

        var request = Request(BackupOperation.Create, proceedOnWarnings: proceedOnWarnings);

        var result = await this.CreateSut().ExecuteAsync(request, this.progress);

        MessageCode[] expectedWarnings = proceedOnWarnings
            ? []
            : [MessageCode.DestinationExistingFilesFormat];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.IsSuccess, Is.EqualTo(proceedOnWarnings));
            Assert.That(Codes(result.Value.Warnings), Is.EqualTo(expectedWarnings));
            Assert.That(result.Value.Errors, Is.Empty);
        }

        var expectedCalls = proceedOnWarnings ? 1 : 0;

        await this.chunkedBackupService.Received(expectedCalls)
            .CreateAsync(SourceDir, DestinationDir, request, this.progress, Arg.Any<CancellationToken>());

        await this.fileOperations.Received(expectedCalls)
            .CleanDirectoryAsync(DestinationDir, Arg.Any<CancellationToken>());
    }

    [TestCase(BackupOperation.Create, true, true)]
    [TestCase(BackupOperation.Create, false, false)]
    [TestCase(BackupOperation.Update, true, false)]
    [TestCase(BackupOperation.Restore, true, false)]
    public async Task ExecuteAsync_DestinationPreparation_CleansOnlyForCreateOverAnExistingDirectory(
        BackupOperation operation,
        bool destinationExists,
        bool expectClean
    )
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(destinationExists);
        this.StubOperationsSucceed();

        _ = await this.CreateSut().ExecuteAsync(Request(operation), this.progress);

        await this.fileOperations.Received(expectClean ? 1 : 0)
            .CleanDirectoryAsync(DestinationDir, Arg.Any<CancellationToken>());
        await this.fileOperations.Received(1)
            .CreateDirectoryAsync(DestinationDir, Arg.Any<CancellationToken>());
    }

    [TestCase(BackupOperation.Create)]
    [TestCase(BackupOperation.Update)]
    [TestCase(BackupOperation.Restore)]
    public async Task ExecuteAsync_KnownOperation_ForwardsNormalizedPathsAndTokenToItsOwnMethod(
        BackupOperation operation
    )
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        this.StubOperationsSucceed();

        using var cancellation = new CancellationTokenSource();
        var request = Request(
            operation,
            source: Unnormalized(SourceDir),
            destination: Unnormalized(DestinationDir)
        );

        var result = await this.CreateSut()
            .ExecuteAsync(request, this.progress, cancellation.Token);

        Assert.That(result.Value.IsSuccess, Is.True);

        await this.chunkedBackupService.Received(operation is BackupOperation.Create ? 1 : 0)
            .CreateAsync(SourceDir, DestinationDir, request, this.progress, cancellation.Token);
        await this.chunkedBackupService.Received(operation is BackupOperation.Update ? 1 : 0)
            .UpdateAsync(SourceDir, DestinationDir, request, this.progress, cancellation.Token);
        await this.chunkedBackupService.Received(operation is BackupOperation.Restore ? 1 : 0)
            .RestoreAsync(SourceDir, DestinationDir, request, this.progress, cancellation.Token);
        await this.chunkedBackupService.DidNotReceive()
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [TestCase(true, MessageCode.SourceMustBeDirectory)]
    [TestCase(false, MessageCode.SourcePathNotExist)]
    public async Task ExecuteAsync_SourceIsAFileOrGone_FailsWithoutPreparingTheDestination(
        bool sourceIsFile,
        MessageCode expectedCode
    )
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(false);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        _ = this.fileOperations.FileExists(SourceDir).Returns(sourceIsFile);

        var result = await this.CreateSut()
            .ExecuteAsync(Request(BackupOperation.Create), this.progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(Codes(result.Errors), Is.EqualTo([expectedCode]));
        }

        await this.fileOperations.DidNotReceive()
            .CleanDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await this.fileOperations.DidNotReceive()
            .CreateDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_UpdateWithMissingDestination_FailsWithoutCreatingADecoyDirectory()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(false);
        this.StubOperationsSucceed();

        var result = await this.CreateSut()
            .ExecuteAsync(Request(BackupOperation.Update), this.progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                Codes(result.Errors),
                Is.EqualTo([MessageCode.BackupDestinationMustExist])
            );
        }

        await this.fileOperations.DidNotReceive()
            .CreateDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await this.chunkedBackupService.DidNotReceive()
            .UpdateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task ExecuteAsync_BackupServiceThrows_ReportsUnexpectedErrorCarryingOnlyTheMessage()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.chunkedBackupService
            .CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await this.CreateSut()
            .ExecuteAsync(Request(BackupOperation.Create), this.progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                Codes(result.Errors),
                Is.EqualTo([MessageCode.UnexpectedErrorFormat])
            );
            Assert.That(result.Errors[0].Args, Is.EqualTo(new object[] { "boom" }));
        }
    }

    [Test]
    public async Task ExecuteAsync_UnrecognizedOperation_FailsLoudlyInsteadOfSilentlyDoingNothing()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        this.StubOperationsSucceed();

        var result = await this.CreateSut()
            .ExecuteAsync(Request((BackupOperation)99), this.progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                Codes(result.Errors),
                Is.EqualTo([MessageCode.UnexpectedErrorFormat])
            );
        }

        await this.chunkedBackupService.DidNotReceive()
            .CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
        await this.chunkedBackupService.DidNotReceive()
            .UpdateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
        await this.chunkedBackupService.DidNotReceive()
            .RestoreAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [TestCase(BackupOperation.Create)]
    [TestCase(BackupOperation.Verify)]
    public void ExecuteAsync_OperationCancelled_PropagatesCancellationInsteadOfMappingIt(
        BackupOperation operation
    )
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        _ = this.chunkedBackupService
            .CreateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new OperationCanceledException());
        _ = this.chunkedBackupService
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new OperationCanceledException());

        _ = Assert.ThrowsAsync<OperationCanceledException>(
            () => this.CreateSut().ExecuteAsync(Request(operation), this.progress)
        );
    }

    [Test]
    public async Task ExecuteAsync_Verify_SkipsValidationAndNeverWritesToADestination()
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.chunkedBackupService
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(SuccessResult());

        var request = Request(BackupOperation.Verify, destination: string.Empty);

        var result = await this.CreateSut().ExecuteAsync(request, this.progress);

        Assert.That(result.Value.IsSuccess, Is.True);

        await this.chunkedBackupService.Received(1)
            .VerifyAsync(SourceDir, request, this.progress, Arg.Any<CancellationToken>());
        await this.validator.DidNotReceive()
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>());
        await this.validator.DidNotReceive()
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>());
        await this.fileOperations.DidNotReceive()
            .CleanDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await this.fileOperations.DidNotReceive()
            .CreateDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteAsync_VerifyWithoutPassword_ReportsPasswordRequiredBeforeAnyProbing()
    {
        var request = Request(
            BackupOperation.Verify,
            destination: string.Empty,
            password: string.Empty
        );

        var result = await this.CreateSut().ExecuteAsync(request, this.progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.IsSuccess, Is.False);
            Assert.That(
                Codes(result.Value.Errors),
                Is.EqualTo([MessageCode.PasswordRequired])
            );
        }

        _ = this.fileOperations.DidNotReceive().DirectoryExists(Arg.Any<string>());
        await this.chunkedBackupService.DidNotReceive()
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task ExecuteAsync_VerifyWithAnUnnormalizablePath_ReportsInvalidPathBeforeProbing()
    {
        var request = Request(
            BackupOperation.Verify,
            source: UnnormalizablePath(),
            destination: string.Empty
        );

        var result = await this.CreateSut().ExecuteAsync(request, this.progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.IsSuccess, Is.False);
            Assert.That(
                Codes(result.Value.Errors),
                Is.EqualTo([MessageCode.InvalidPathFormat]),
                "verify skips the request validator entirely, so this check is the only thing standing between "
                    + "an unresolvable path and a raw exception from the file-system probe below it"
            );
        }

        _ = this.fileOperations.DidNotReceive().DirectoryExists(Arg.Any<string>());
        await this.chunkedBackupService.DidNotReceive()
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [TestCase(true, MessageCode.SourceMustBeDirectory)]
    [TestCase(false, MessageCode.SourcePathNotExist)]
    public async Task ExecuteAsync_VerifySourceIsAFileOrGone_FailsWithoutReadingTheArchive(
        bool sourceIsFile,
        MessageCode expectedCode
    )
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(false);
        _ = this.fileOperations.FileExists(SourceDir).Returns(sourceIsFile);

        var result = await this.CreateSut()
            .ExecuteAsync(Request(BackupOperation.Verify), this.progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                Codes(result.Errors),
                Is.EqualTo([expectedCode]),
                "a backup is a directory of chunks, so pointing verify at a single file is a different user "
                    + "mistake from pointing it at nothing, and telling the two apart is the whole message"
            );
        }

        await this.chunkedBackupService.DidNotReceive()
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task ExecuteAsync_VerifyThrows_ReportsUnexpectedErrorCarryingOnlyTheMessage()
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.chunkedBackupService
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new InvalidOperationException("verify boom"));

        var result = await this.CreateSut()
            .ExecuteAsync(Request(BackupOperation.Verify), this.progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                Codes(result.Errors),
                Is.EqualTo([MessageCode.UnexpectedErrorFormat])
            );
            Assert.That(result.Errors[0].Args, Is.EqualTo(new object[] { "verify boom" }));
        }
    }

    [Test]
    public async Task ExecuteAsync_UnnormalizablePaths_FailOnTheRawPathInsteadOfThrowing()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);
        this.StubOperationsSucceed();

        var request = Request(
            BackupOperation.Create,
            source: UnnormalizablePath(),
            destination: UnnormalizablePath()
        );

        var result = await this.CreateSut().ExecuteAsync(request, this.progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(
                Codes(result.Errors),
                Is.EqualTo([MessageCode.SourcePathNotExist]),
                "normalization failing must not swap the path for something else: the raw value is probed, "
                    + "found missing, and reported, because silently substituting a resolvable path would point "
                    + "a destructive create at a directory the user never named"
            );
        }

        _ = this.fileOperations.Received(1).DirectoryExists(request.SourcePath);
        await this.fileOperations.DidNotReceive()
            .CleanDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await this.fileOperations.DidNotReceive()
            .CreateDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
