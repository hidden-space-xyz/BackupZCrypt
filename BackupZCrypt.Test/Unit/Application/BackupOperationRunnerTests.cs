using BackupZCrypt.Application.Orchestrators;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Utilities.Helpers;
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
/// Unit tests for the backup operation runner: the validation gate, the post-validation source and
/// destination checks, destination preparation (including the destructive clean a create performs),
/// operation dispatch, cancellation, and unexpected-error mapping. The validator, file system, and
/// backup service are all substituted, so nothing here touches the real disk.
/// </summary>
/// <remarks>
/// A create over an existing destination cleans that directory first, which is the one irreversible act
/// the runner performs: a request the user never accepted — one blocked by validation errors, by
/// warnings they declined, or aimed at a path that failed to normalize — must never reach the clean and
/// wipe a directory that already holds their data. The assertions that a clean was never received are
/// guarding exactly that, so none of them is redundant with the positive case beside it.
/// </remarks>
public sealed class BackupOperationRunnerTests
{
    /// <summary>
    /// The rooted source path the requests point at. Nothing is created on disk because the file
    /// system is substituted, but the path must be absolute to survive path normalization.
    /// </summary>
    private static readonly string SourceDir = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "bzc-runner-src")
    );

    /// <summary>
    /// The rooted destination path the requests point at, kept distinct from <see cref="SourceDir"/>.
    /// </summary>
    private static readonly string DestinationDir = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "bzc-runner-dst")
    );

    /// <summary>
    /// The substituted validator, which lets a test choose exactly which blocking errors and advisory
    /// warnings the runner sees without depending on the real validator's file-system probes.
    /// </summary>
    private readonly IBackupRequestValidator validator =
        Substitute.For<IBackupRequestValidator>();

    /// <summary>
    /// The substituted file system the runner probes for the source and destination, and asks to
    /// clean and create the destination directory.
    /// </summary>
    private readonly IFileOperationsService fileOperations =
        Substitute.For<IFileOperationsService>();

    /// <summary>
    /// The substituted backup engine the runner dispatches each operation to.
    /// </summary>
    private readonly IChunkedBackupService chunkedBackupService =
        Substitute.For<IChunkedBackupService>();

    /// <summary>
    /// The progress sink handed to the runner; assertions check it is forwarded unchanged.
    /// </summary>
    private readonly RecordingProgress<BackupStatus> progress = new();

    /// <summary>
    /// Creates a runner wired to the substituted validator, file system, and backup service.
    /// </summary>
    /// <returns>The system under test.</returns>
    private BackupOperationRunner CreateSut()
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
    /// a test prove the runner hands the resolved path to the backup service.
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

    [Fact]
    internal async Task RunAsync_ValidationErrors_FailsAndNeverStartsTheBackup()
    {
        _ = this.validator
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(Messages(MessageCode.PasswordTooShort));
        _ = this.fileOperations.DirectoryExists(Arg.Any<string>()).Returns(true);

        var result = await this.CreateSut()
            .RunAsync(Request(BackupOperation.Create), this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () =>
                Assert.Equal<MessageCode>(
                    [MessageCode.PasswordTooShort],
                    Codes(result.Errors)
                )
        );

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    internal async Task RunAsync_WarningsRaised_RunsOnlyWhenTheUserAgreedToProceed(
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

        var result = await this.CreateSut().RunAsync(request, this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.True(result.IsSuccess),
            () => Assert.Equal(!proceedOnWarnings, result.Value.NeedsWarningConfirmation)
        );

        if (proceedOnWarnings)
        {
            Assert.Multiple(
                () => Assert.True(result.Value.Completion!.IsSuccess),
                () => Assert.Empty(result.Value.PendingWarnings)
            );
        }
        else
        {
            Assert.Multiple(
                () => Assert.Null(result.Value.Completion),
                () =>
                    Assert.Equal<MessageCode>(
                        [MessageCode.DestinationExistingFilesFormat],
                        Codes(result.Value.PendingWarnings)
                    )
            );
        }

        var expectedCalls = proceedOnWarnings ? 1 : 0;

        await this.chunkedBackupService.Received(expectedCalls)
            .CreateAsync(SourceDir, DestinationDir, request, this.progress, Arg.Any<CancellationToken>());

        await this.fileOperations.Received(expectedCalls)
            .CleanDirectoryAsync(DestinationDir, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(BackupOperation.Create, true, true)]
    [InlineData(BackupOperation.Create, false, false)]
    [InlineData(BackupOperation.Update, true, false)]
    [InlineData(BackupOperation.Restore, true, false)]
    internal async Task RunAsync_DestinationPreparation_CleansOnlyForCreateOverAnExistingDirectory(
        BackupOperation operation,
        bool destinationExists,
        bool expectClean
    )
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(destinationExists);
        this.StubOperationsSucceed();

        _ = await this.CreateSut().RunAsync(Request(operation), this.progress, CancellationToken.None);

        await this.fileOperations.Received(expectClean ? 1 : 0)
            .CleanDirectoryAsync(DestinationDir, Arg.Any<CancellationToken>());
        await this.fileOperations.Received(1)
            .CreateDirectoryAsync(DestinationDir, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(BackupOperation.Create)]
    [InlineData(BackupOperation.Update)]
    [InlineData(BackupOperation.Restore)]
    internal async Task RunAsync_KnownOperation_ForwardsNormalizedPathsAndTokenToItsOwnMethod(
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

        var result = await this.CreateSut().RunAsync(request, this.progress, cancellation.Token);

        Assert.True(result.Value.Completion!.IsSuccess);

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

    [Fact]
    internal async Task RunAsync_NoProgressSink_ForwardsTheSharedNullSinkToTheEngine()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        this.StubOperationsSucceed();

        _ = await this.CreateSut()
            .RunAsync(Request(BackupOperation.Create), null, CancellationToken.None);

        await this.chunkedBackupService.Received(1)
            .CreateAsync(
                SourceDir,
                DestinationDir,
                Arg.Any<BackupRequest>(),
                NullProgress<BackupStatus>.Instance,
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(true, MessageCode.SourceMustBeDirectory)]
    [InlineData(false, MessageCode.SourcePathNotExist)]
    internal async Task RunAsync_SourceIsAFileOrGone_FailsWithoutPreparingTheDestination(
        bool sourceIsFile,
        MessageCode expectedCode
    )
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(false);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        _ = this.fileOperations.FileExists(SourceDir).Returns(sourceIsFile);

        var result = await this.CreateSut()
            .RunAsync(Request(BackupOperation.Create), this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () => Assert.Equal<MessageCode>([expectedCode], Codes(result.Errors))
        );

        await this.fileOperations.DidNotReceive()
            .CleanDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await this.fileOperations.DidNotReceive()
            .CreateDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    internal async Task RunAsync_UpdateWithMissingDestination_FailsWithoutCreatingADecoyDirectory()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(false);
        this.StubOperationsSucceed();

        var result = await this.CreateSut()
            .RunAsync(Request(BackupOperation.Update), this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () =>
                Assert.Equal<MessageCode>(
                    [MessageCode.BackupDestinationMustExist],
                    Codes(result.Errors)
                )
        );

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

    [Fact]
    internal async Task RunAsync_BackupServiceThrows_ReportsUnexpectedErrorCarryingOnlyTheMessage()
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
            .RunAsync(Request(BackupOperation.Create), this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () =>
                Assert.Equal<MessageCode>(
                    [MessageCode.UnexpectedErrorFormat],
                    Codes(result.Errors)
                ),
            () => Assert.Equal<object>(["boom"], result.Errors[0].Args)
        );
    }

    [Fact]
    internal async Task RunAsync_UnrecognizedOperation_FailsLoudlyInsteadOfSilentlyDoingNothing()
    {
        this.PassValidation();
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        this.StubOperationsSucceed();

        var result = await this.CreateSut()
            .RunAsync(Request((BackupOperation)99), this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () =>
                Assert.Equal<MessageCode>(
                    [MessageCode.UnexpectedErrorFormat],
                    Codes(result.Errors)
                )
        );

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

    [Fact]
    internal async Task RunAsync_OperationCancelled_PropagatesCancellationInsteadOfMappingIt()
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

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => this.CreateSut()
                .RunAsync(Request(BackupOperation.Create), this.progress, CancellationToken.None)
        );
    }

    [Fact]
    internal async Task RunVerifyAsync_OperationCancelled_PropagatesCancellationInsteadOfMappingIt()
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.chunkedBackupService
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new OperationCanceledException());

        _ = await Assert.ThrowsAsync<OperationCanceledException>(
            () => this.CreateSut()
                .RunVerifyAsync(Request(BackupOperation.Verify), this.progress, CancellationToken.None)
        );
    }

    [Fact]
    internal async Task RunVerifyAsync_ExistingArchive_SkipsValidationAndNeverWritesToADestination()
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

        var result = await this.CreateSut()
            .RunVerifyAsync(request, this.progress, CancellationToken.None);

        Assert.True(result.Value.Completion!.IsSuccess);

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

    [Fact]
    internal async Task RunVerifyAsync_WithoutPassword_ReportsPasswordRequiredBeforeAnyProbing()
    {
        var request = Request(
            BackupOperation.Verify,
            destination: string.Empty,
            password: string.Empty
        );

        var result = await this.CreateSut()
            .RunVerifyAsync(request, this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () =>
                Assert.Equal<MessageCode>(
                    [MessageCode.PasswordRequired],
                    Codes(result.Errors)
                )
        );

        _ = this.fileOperations.DidNotReceive().DirectoryExists(Arg.Any<string>());
        await this.chunkedBackupService.DidNotReceive()
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    internal async Task RunVerifyAsync_WithAnUnnormalizablePath_ReportsInvalidPathBeforeProbing()
    {
        var request = Request(
            BackupOperation.Verify,
            source: UnnormalizablePath(),
            destination: string.Empty
        );

        var result = await this.CreateSut()
            .RunVerifyAsync(request, this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () =>
                Assert.Equal<MessageCode>(
                    [MessageCode.InvalidPathFormat],
                    Codes(result.Errors)
                )
        );

        _ = this.fileOperations.DidNotReceive().DirectoryExists(Arg.Any<string>());
        await this.chunkedBackupService.DidNotReceive()
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(true, MessageCode.SourceMustBeDirectory)]
    [InlineData(false, MessageCode.SourcePathNotExist)]
    internal async Task RunVerifyAsync_SourceIsAFileOrGone_FailsWithoutReadingTheArchive(
        bool sourceIsFile,
        MessageCode expectedCode
    )
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(false);
        _ = this.fileOperations.FileExists(SourceDir).Returns(sourceIsFile);

        var result = await this.CreateSut()
            .RunVerifyAsync(Request(BackupOperation.Verify), this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () => Assert.Equal<MessageCode>([expectedCode], Codes(result.Errors))
        );

        await this.chunkedBackupService.DidNotReceive()
            .VerifyAsync(
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    internal async Task RunVerifyAsync_EngineThrows_ReportsUnexpectedErrorCarryingOnlyTheMessage()
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
            .RunVerifyAsync(Request(BackupOperation.Verify), this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () =>
                Assert.Equal<MessageCode>(
                    [MessageCode.UnexpectedErrorFormat],
                    Codes(result.Errors)
                ),
            () => Assert.Equal<object>(["verify boom"], result.Errors[0].Args)
        );
    }

    [Fact]
    internal async Task RunAsync_UnnormalizablePaths_FailOnTheRawPathInsteadOfThrowing()
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

        var result = await this.CreateSut().RunAsync(request, this.progress, CancellationToken.None);

        Assert.Multiple(
            () => Assert.False(result.IsSuccess),
            () =>
                Assert.Equal<MessageCode>(
                    [MessageCode.SourcePathNotExist],
                    Codes(result.Errors)
                )
        );

        _ = this.fileOperations.Received(1).DirectoryExists(request.SourcePath);
        await this.fileOperations.DidNotReceive()
            .CleanDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await this.fileOperations.DidNotReceive()
            .CreateDirectoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
