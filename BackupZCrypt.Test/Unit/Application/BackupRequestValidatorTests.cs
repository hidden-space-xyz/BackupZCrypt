using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Password;
using BackupZCrypt.Application.Validators;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;

using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the backup request validator's blocking errors and advisory warnings.
/// </summary>
/// <remarks>
/// Blocking errors stop a request, so they are asserted exactly. Warnings are only advisory and the
/// orchestrator does not guard the call that produces them, so every probe the warning sweep makes has to
/// degrade into "no warning" rather than let an exception escape as an unhandled crash. Several warning
/// tests therefore hand the substituted password service a password it rates as weak: the weak-password
/// warning still arriving is what proves the sweep ran to the end instead of being cut short by a failing
/// probe.
/// </remarks>
public sealed class BackupRequestValidatorTests
{
    /// <summary>
    /// The rooted source path the requests point at. Nothing is created on disk because the file
    /// system is substituted, but the path must be absolute to survive path normalization.
    /// </summary>
    private static readonly string SourceDir = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "bzc-validator-src")
    );

    /// <summary>
    /// The rooted destination path the requests point at, kept distinct from <see cref="SourceDir"/>
    /// so overlap checks only fire when a test asks for it.
    /// </summary>
    private static readonly string DestinationDir = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "bzc-validator-dst")
    );

    /// <summary>
    /// The substituted file system the validator probes for path existence, for the source and
    /// destination file listings, and for the file sizes behind the free-space estimate.
    /// </summary>
    private readonly IFileOperationsService fileOperations =
        Substitute.For<IFileOperationsService>();

    /// <summary>
    /// The substituted storage service the validator queries for the destination drive root, that
    /// drive's readiness, and its available free space.
    /// </summary>
    private readonly ISystemStorageService systemStorage = Substitute.For<ISystemStorageService>();

    /// <summary>
    /// The substituted password service that supplies the strength analysis behind weak-password warnings.
    /// </summary>
    private readonly IPasswordService passwordService = Substitute.For<IPasswordService>();

    /// <summary>
    /// Creates a validator wired to the substituted file, storage, and password services.
    /// </summary>
    /// <returns>The system under test.</returns>
    private BackupRequestValidator CreateSut() =>
        new(this.fileOperations, this.systemStorage, this.passwordService);

    /// <summary>
    /// Builds a request whose fields are each individually valid, so a test only varies the one it
    /// exercises. Whether validation actually reports nothing still depends on the substitute setup.
    /// </summary>
    /// <param name="source">The source path to validate.</param>
    /// <param name="destination">The destination path to validate.</param>
    /// <param name="password">The password, also used as the confirmation so the two always match.</param>
    /// <param name="operation">The operation the request asks for.</param>
    /// <returns>An AES plus Argon2id request built from the supplied values.</returns>
    private static BackupRequest ValidRequest(
        string source,
        string destination,
        string password = "Str0ng-Passw0rd!",
        BackupOperation operation = BackupOperation.Create
    ) =>
        new(
            source,
            destination,
            password,
            password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            operation
        );

    /// <summary>
    /// Projects validation messages down to their codes so assertions ignore format arguments.
    /// </summary>
    /// <param name="messages">The errors or warnings returned by the validator.</param>
    /// <returns>The code of each message, in the order reported.</returns>
    private static IReadOnlyList<MessageCode> Codes(IReadOnlyList<LocalizableMessage> messages) =>
        messages.Select(m => m.Code).ToList();

    /// <summary>
    /// Builds a path the running platform cannot resolve to an absolute form: one longer than
    /// Windows can address, and one carrying an embedded null character everywhere else.
    /// </summary>
    /// <returns>A raw path that fails normalization.</returns>
    private static string UnnormalizablePath() =>
        OperatingSystem.IsWindows() ? new string('a', 300_000) : "some-folder\0name";

    [Test]
    public async Task AnalyzeErrors_EmptySourcePath_ReportsSourcePathEmpty()
    {
        var request = ValidRequest(string.Empty, DestinationDir);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var errors = await this.CreateSut().AnalyzeErrorsAsync(request);

        Assert.That(Codes(errors), Does.Contain(MessageCode.SourcePathEmpty));
    }

    [Test]
    public async Task AnalyzeErrors_SourceNeitherFileNorDirectory_ReportsNotExist()
    {
        _ = this.fileOperations.FileExists(SourceDir).Returns(false);
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(false);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var request = ValidRequest(SourceDir, DestinationDir);

        var errors = await this.CreateSut().AnalyzeErrorsAsync(request);

        Assert.That(Codes(errors), Does.Contain(MessageCode.SourcePathNotExistFormat));
    }

    [Test]
    public async Task AnalyzeErrors_EmptyPassword_ReportsPasswordRequired()
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Path.Combine(SourceDir, "a.txt")]);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var request = ValidRequest(SourceDir, DestinationDir, password: string.Empty);

        var errors = await this.CreateSut().AnalyzeErrorsAsync(request);

        Assert.That(Codes(errors), Does.Contain(MessageCode.PasswordRequired));
    }

    [Test]
    public async Task AnalyzeErrors_ShortPassword_ReportsPasswordTooShort()
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Path.Combine(SourceDir, "a.txt")]);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var request = ValidRequest(SourceDir, DestinationDir, password: "Ab1!xyz");

        var errors = await this.CreateSut().AnalyzeErrorsAsync(request);

        Assert.That(Codes(errors), Does.Contain(MessageCode.PasswordTooShort));
    }

    [Test]
    public async Task AnalyzeErrors_PasswordMismatch_ReportsMismatch()
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Path.Combine(SourceDir, "a.txt")]);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var request = new BackupRequest(
            SourceDir,
            DestinationDir,
            "Str0ng-Passw0rd!",
            "Different-Passw0rd!",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            BackupOperation.Create
        );

        var errors = await this.CreateSut().AnalyzeErrorsAsync(request);

        Assert.That(Codes(errors), Does.Contain(MessageCode.PasswordMismatch));
    }

    [Test]
    public async Task AnalyzeErrors_SourceEqualsDestinationDirectory_ReportsSameDirectory()
    {
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Path.Combine(SourceDir, "a.txt")]);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var request = ValidRequest(SourceDir, SourceDir);

        var errors = await this.CreateSut().AnalyzeErrorsAsync(request);

        Assert.That(Codes(errors), Does.Contain(MessageCode.SourceDestinationSameDirectory));
    }

    [Test]
    public async Task AnalyzeErrors_FullyValidRequest_ReturnsEmpty()
    {
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(false);
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Path.Combine(SourceDir, "a.txt")]);

        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns("C:\\");
        _ = this.systemStorage.IsDriveReady("C:\\").Returns(true);

        var request = ValidRequest(SourceDir, DestinationDir);

        var errors = await this.CreateSut().AnalyzeErrorsAsync(request);

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public async Task AnalyzeWarnings_WeakPassword_ReportsWeakPasswordWarning()
    {
        _ = this.fileOperations.DirectoryExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);

        _ = this.passwordService
            .AnalyzePasswordStrength(Arg.Any<string>())
            .Returns(new PasswordStrengthAnalysis(PasswordStrength.Weak, 20, 10, []));

        var request = ValidRequest(SourceDir, DestinationDir);

        var warnings = await this.CreateSut().AnalyzeWarningsAsync(request);

        Assert.That(Codes(warnings), Does.Contain(MessageCode.WeakPasswordWarning));
    }

    [Test]
    public async Task AnalyzeWarnings_StrongPassword_DoesNotReportWeakPasswordWarning()
    {
        _ = this.fileOperations.DirectoryExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);

        _ = this.passwordService
            .AnalyzePasswordStrength(Arg.Any<string>())
            .Returns(new PasswordStrengthAnalysis(PasswordStrength.Strong, 95, 110, []));

        var request = ValidRequest(SourceDir, DestinationDir);

        var warnings = await this.CreateSut().AnalyzeWarningsAsync(request);

        Assert.That(Codes(warnings), Does.Not.Contain(MessageCode.WeakPasswordWarning));
    }

    [Test]
    public async Task AnalyzeWarnings_InsufficientDiskSpace_ReportsLowDiskSpace()
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(false);
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);

        var sourceFile = Path.Combine(SourceDir, "big.bin");
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([sourceFile]);
        _ = this.fileOperations.GetFileSize(sourceFile).Returns(1_000_000L);

        _ = this.systemStorage.GetPathRoot(DestinationDir).Returns("C:\\");
        _ = this.systemStorage.IsDriveReady("C:\\").Returns(true);
        _ = this.systemStorage.GetAvailableFreeSpace("C:\\").Returns(500_000L);

        _ = this.passwordService
            .AnalyzePasswordStrength(Arg.Any<string>())
            .Returns(new PasswordStrengthAnalysis(PasswordStrength.Strong, 95, 110, []));

        var request = ValidRequest(SourceDir, DestinationDir);

        var warnings = await this.CreateSut().AnalyzeWarningsAsync(request);

        Assert.That(Codes(warnings), Does.Contain(MessageCode.LowDiskSpaceFormat));
    }

    [TestCase(BackupOperation.Create, true)]
    [TestCase(BackupOperation.Restore, true)]
    [TestCase(BackupOperation.Update, false)]
    public async Task AnalyzeWarnings_ExistingDestinationFiles_WarnsForCreateAndRestoreOnly(
        BackupOperation operation,
        bool expectedWarning
    )
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(false);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(true);
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations
            .GetFilesAsync(DestinationDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Path.Combine(DestinationDir, "existing.bin")]);

        _ = this.passwordService
            .AnalyzePasswordStrength(Arg.Any<string>())
            .Returns(new PasswordStrengthAnalysis(PasswordStrength.Strong, 95, 110, []));

        var request = ValidRequest(SourceDir, DestinationDir, operation: operation);

        var warnings = await this.CreateSut().AnalyzeWarningsAsync(request);

        var hasWarning = Codes(warnings).Contains(MessageCode.DestinationExistingFilesFormat);
        Assert.That(hasWarning, Is.EqualTo(expectedWarning));
    }

    /// <summary>
    /// Makes the substituted file system report a readable, non-empty source directory so a test can
    /// vary only the destination or the password without tripping a source error.
    /// </summary>
    private void StubReadableSource()
    {
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Path.Combine(SourceDir, "a.txt")]);
    }

    [Test]
    public async Task AnalyzeErrors_SourceIsAFile_ReportsMustBeDirectoryWithoutListingIt()
    {
        _ = this.fileOperations.FileExists(SourceDir).Returns(true);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(ValidRequest(SourceDir, DestinationDir));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Codes(errors), Does.Contain(MessageCode.SourceMustBeDirectory));
            Assert.That(Codes(errors), Does.Not.Contain(MessageCode.SourcePathNotExistFormat));
        }

        await this.fileOperations.DidNotReceive()
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AnalyzeErrors_SourceDirectoryHasNoFiles_ReportsSourceDirectoryEmpty()
    {
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(ValidRequest(SourceDir, DestinationDir));

        Assert.That(Codes(errors), Is.EqualTo(new[] { MessageCode.SourceDirectoryEmpty }));
    }

    /// <summary>
    /// Supplies the exception thrown while listing the source, the error code it must map to, and the
    /// format arguments that message is allowed to carry.
    /// </summary>
    /// <remarks>
    /// The permission-denied arm carries no arguments at all, so the raw .NET message — which embeds the
    /// full path on Windows — never reaches the user-facing dialog.
    /// </remarks>
    /// <returns>One case per catch arm of the source listing block.</returns>
    private static IEnumerable<TestCaseData> SourceListingFailureCases()
    {
        yield return new TestCaseData(
            new UnauthorizedAccessException("denied"),
            MessageCode.SourceAccessDenied,
            Array.Empty<object>()
        );

        yield return new TestCaseData(
            new IOException("drive fell over"),
            MessageCode.SourceAccessErrorFormat,
            new object[] { "drive fell over" }
        );
    }

    [TestCaseSource(nameof(SourceListingFailureCases))]
    public async Task AnalyzeErrors_SourceListingThrows_ReportsTheMatchingAccessError(
        Exception thrown,
        MessageCode expectedCode,
        object[] expectedArgs
    )
    {
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(thrown);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(ValidRequest(SourceDir, DestinationDir));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Codes(errors), Is.EqualTo(new[] { expectedCode }));
            Assert.That(errors[0].Args, Is.EqualTo(expectedArgs));
        }
    }

    [Test]
    public async Task AnalyzeErrors_EmptyDestinationPath_ReportsDestinationPathEmptyWithoutProbing()
    {
        this.StubReadableSource();

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(ValidRequest(SourceDir, string.Empty));

        Assert.That(Codes(errors), Is.EqualTo(new[] { MessageCode.DestinationPathEmpty }));

        _ = this.systemStorage.DidNotReceive().GetPathRoot(Arg.Any<string>());
    }

    [Test]
    public async Task AnalyzeErrors_DestinationDriveNotReady_ReportsDriveNotAccessibleWithTheRoot()
    {
        var driveRoot = Path.GetPathRoot(DestinationDir)!;

        this.StubReadableSource();
        _ = this.systemStorage.GetPathRoot(DestinationDir).Returns(driveRoot);
        _ = this.systemStorage.IsDriveReady(driveRoot).Returns(false);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(ValidRequest(SourceDir, DestinationDir));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Codes(errors),
                Is.EqualTo(new[] { MessageCode.DestinationDriveNotAccessibleFormat })
            );
            Assert.That(errors[0].Args, Is.EqualTo(new object[] { driveRoot }));
        }
    }

    [Test]
    public async Task AnalyzeErrors_DestinationRootUnknown_ReportsNothingAndSkipsTheReadinessProbe()
    {
        this.StubReadableSource();
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns((string?)null);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(ValidRequest(SourceDir, DestinationDir));

        Assert.That(errors, Is.Empty);

        _ = this.systemStorage.DidNotReceive().IsDriveReady(Arg.Any<string>());
    }

    [Test]
    public async Task AnalyzeErrors_DestinationRootLookupThrows_ReportsDestinationInvalidWithMessage()
    {
        this.StubReadableSource();
        _ = this.systemStorage
            .GetPathRoot(DestinationDir)
            .Returns(_ => throw new ArgumentException("bad root"));

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(ValidRequest(SourceDir, DestinationDir));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Codes(errors), Is.EqualTo(new[] { MessageCode.DestinationInvalidFormat }));
            Assert.That(errors[0].Args, Is.EqualTo(new object[] { "bad root" }));
        }
    }

    [TestCase(true, false, 1)]
    [TestCase(false, true, 1)]
    [TestCase(true, true, 2)]
    public async Task AnalyzeErrors_UnnormalizablePath_ReportsOneInvalidPathFormatPerBadPath(
        bool sourceInvalid,
        bool destinationInvalid,
        int expectedCount
    )
    {
        var request = new BackupRequest(
            sourceInvalid ? UnnormalizablePath() : SourceDir,
            destinationInvalid ? UnnormalizablePath() : DestinationDir,
            string.Empty,
            string.Empty,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            BackupOperation.Create
        );

        var errors = await this.CreateSut().AnalyzeErrorsAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                errors,
                Has.Count.EqualTo(expectedCount),
                "an unresolvable path ends validation early, so only the path problem is reported: the empty "
                    + "password on this request is never even looked at, because nothing else can be validated "
                    + "against a path that failed to resolve"
            );
            Assert.That(Codes(errors), Is.All.EqualTo(MessageCode.InvalidPathFormat));
            Assert.That(errors[0].Args, Has.Count.EqualTo(1));
        }

        _ = this.fileOperations.DidNotReceive().DirectoryExists(Arg.Any<string>());
        _ = this.fileOperations.DidNotReceive().FileExists(Arg.Any<string>());
    }

    [TestCase(1000, false)]
    [TestCase(1001, true)]
    public async Task AnalyzeErrors_PasswordLength_ReportsTooLongOnlyBeyondTheLimit(
        int length,
        bool expectTooLong
    )
    {
        this.StubReadableSource();
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var password = new string('x', length);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(ValidRequest(SourceDir, DestinationDir, password: password));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Codes(errors).Contains(MessageCode.PasswordTooLong),
                Is.EqualTo(expectTooLong)
            );
            Assert.That(Codes(errors), Does.Not.Contain(MessageCode.PasswordTooShort));
        }
    }

    [TestCase(" Str0ng-Passw0rd!", true)]
    [TestCase("Str0ng-Passw0rd! ", true)]
    [TestCase("\tStr0ng-Passw0rd!", true)]
    [TestCase("Str0ng Passw0rd!", false)]
    public async Task AnalyzeErrors_Password_RejectsOnlySurroundingWhitespace(
        string password,
        bool expectRejected
    )
    {
        this.StubReadableSource();
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(ValidRequest(SourceDir, DestinationDir, password: password));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Codes(errors).Contains(MessageCode.PasswordLeadingTrailingSpaces),
                Is.EqualTo(expectRejected)
            );
            Assert.That(Codes(errors), Does.Not.Contain(MessageCode.PasswordMismatch));
        }
    }

    [TestCase(BackupOperation.Create, "", MessageCode.ConfirmPasswordRequired, true)]
    [TestCase(BackupOperation.Create, "Different-Passw0rd!", MessageCode.PasswordMismatch, true)]
    [TestCase(BackupOperation.Restore, "", MessageCode.ConfirmPasswordRequired, false)]
    [TestCase(BackupOperation.Update, "Different-Passw0rd!", MessageCode.PasswordMismatch, false)]
    public async Task AnalyzeErrors_ConfirmPassword_IsEnforcedOnCreateOnly(
        BackupOperation operation,
        string confirmPassword,
        MessageCode expectedCode,
        bool expectReported
    )
    {
        this.StubReadableSource();
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var request = new BackupRequest(
            SourceDir,
            DestinationDir,
            "Str0ng-Passw0rd!",
            confirmPassword,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            operation
        );

        var errors = await this.CreateSut().AnalyzeErrorsAsync(request);

        Assert.That(Codes(errors).Contains(expectedCode), Is.EqualTo(expectReported));
    }

    /// <summary>
    /// Supplies source and destination pairs together with the containment errors they must produce,
    /// including a sibling pair that shares a name prefix and must produce none.
    /// </summary>
    /// <returns>One case per containment outcome.</returns>
    private static IEnumerable<TestCaseData> OverlappingPathCases()
    {
        yield return new TestCaseData(
            SourceDir,
            Path.Combine(SourceDir, "backup"),
            new[] { MessageCode.DestinationInsideSource }
        );

        yield return new TestCaseData(
            Path.Combine(DestinationDir, "data"),
            DestinationDir,
            new[] { MessageCode.SourceInsideDestination }
        );

        yield return new TestCaseData(
            SourceDir,
            SourceDir + "-backup",
            Array.Empty<MessageCode>()
        );
    }

    [TestCaseSource(nameof(OverlappingPathCases))]
    public async Task AnalyzeErrors_SourceAndDestinationOverlap_ReportsTheMatchingContainmentError(
        string source,
        string destination,
        MessageCode[] expectedCodes
    )
    {
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations.DirectoryExists(source).Returns(true);
        _ = this.fileOperations
            .GetFilesAsync(source, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Path.Combine(source, "a.txt")]);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var errors = await this.CreateSut().AnalyzeErrorsAsync(ValidRequest(source, destination));

        var containment = Codes(errors)
            .Where(c =>
                c is MessageCode.SourceDestinationSameDirectory
                    or MessageCode.DestinationInsideSource
                    or MessageCode.SourceInsideDestination
            )
            .ToList();

        Assert.That(containment, Is.EqualTo(expectedCodes));
    }

    [Test]
    public async Task AnalyzeErrors_OverlapProbeThrows_ReportsNoContainmentErrorAtAll()
    {
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations
            .DirectoryExists(SourceDir)
            .Returns(_ => true, _ => throw new IOException("probe failed"));
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Path.Combine(SourceDir, "a.txt")]);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var errors = await this.CreateSut().AnalyzeErrorsAsync(ValidRequest(SourceDir, SourceDir));

        Assert.That(
            errors,
            Is.Empty,
            "the first directory probe answers the source check and the second is the overlap probe, which is "
                + "best effort so an unreadable path never blocks a backup with a spurious error: this request "
                + "points at a single directory, so a probe that did not degrade gracefully would report "
                + "SourceDestinationSameDirectory here"
        );
    }

    [Test]
    public async Task AnalyzeErrors_DestinationDiffersFromSourceByCaseOnly_FollowsThePlatformRule()
    {
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Path.Combine(SourceDir, "a.txt")]);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(ValidRequest(SourceDir, SourceDir.ToUpperInvariant()));

        MessageCode[] expected = OperatingSystem.IsWindows()
            ? [MessageCode.SourceDestinationSameDirectory]
            : [];

        Assert.That(
            Codes(errors),
            Is.EqualTo(expected),
            "Windows resolves both spellings to the same directory, so writing the backup into it has to be "
                + "rejected; on a case-sensitive file system they are two different directories and rejecting "
                + "them would block a perfectly valid backup"
        );
    }

    [Test]
    public async Task AnalyzeWarnings_SourceListingThrows_ReturnsEmptyInsteadOfPropagating()
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("gone"));

        var warnings = await this.CreateSut()
            .AnalyzeWarningsAsync(ValidRequest(SourceDir, DestinationDir));

        Assert.That(
            warnings,
            Is.Empty,
            "swallowing the listing failure also suppresses the weak-password check that would otherwise follow"
        );
    }

    [TestCase(-1L, false)]
    [TestCase(1_200_000L, false)]
    [TestCase(1_199_999L, true)]
    public async Task AnalyzeWarnings_FreeSpace_WarnsOnlyWhenAKnownAmountFallsShort(
        long availableBytes,
        bool expectWarning
    )
    {
        var driveRoot = Path.GetPathRoot(DestinationDir)!;

        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(false);

        var sourceFile = Path.Combine(SourceDir, "big.bin");
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([sourceFile]);
        _ = this.fileOperations.GetFileSize(sourceFile).Returns(1_000_000L);

        _ = this.systemStorage.GetPathRoot(DestinationDir).Returns(driveRoot);
        _ = this.systemStorage.IsDriveReady(driveRoot).Returns(true);
        _ = this.systemStorage.GetAvailableFreeSpace(driveRoot).Returns(availableBytes);

        _ = this.passwordService
            .AnalyzePasswordStrength(Arg.Any<string>())
            .Returns(new PasswordStrengthAnalysis(PasswordStrength.Strong, 95, 110, []));

        var warnings = await this.CreateSut()
            .AnalyzeWarningsAsync(ValidRequest(SourceDir, DestinationDir));

        Assert.That(
            Codes(warnings).Contains(MessageCode.LowDiskSpaceFormat),
            Is.EqualTo(expectWarning),
            "1,000,000 bytes of source needs 1,200,000 bytes free, and -1 means the free space cannot be "
                + "queried at all: comparing that sentinel as a number turns an unqueryable volume into an "
                + "unusable one"
        );
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task AnalyzeWarnings_UnnormalizablePath_ReturnsEmptyWithoutTouchingTheFileSystem(
        bool sourceInvalid,
        bool destinationInvalid
    )
    {
        _ = this.passwordService
            .AnalyzePasswordStrength(Arg.Any<string>())
            .Returns(new PasswordStrengthAnalysis(PasswordStrength.Weak, 20, 10, []));

        var request = new BackupRequest(
            sourceInvalid ? UnnormalizablePath() : SourceDir,
            destinationInvalid ? UnnormalizablePath() : DestinationDir,
            "weak",
            "weak",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            BackupOperation.Create
        );

        var warnings = await this.CreateSut().AnalyzeWarningsAsync(request);

        Assert.That(
            warnings,
            Is.Empty,
            "an unresolvable path is the blocking errors' business, so the sweep stops before its first probe "
                + "rather than estimate free space and list files for a path already known to be unusable: the "
                + "weak password would otherwise have warned on its own"
        );

        _ = this.fileOperations.DidNotReceive().DirectoryExists(Arg.Any<string>());
        _ = this.passwordService.DidNotReceive().AnalyzePasswordStrength(Arg.Any<string>());
    }

    [Test]
    public async Task AnalyzeWarnings_FileSizeProbeThrows_CountsThatFileAsZeroAndKeepsSweeping()
    {
        var driveRoot = Path.GetPathRoot(DestinationDir)!;
        var readable = Path.Combine(SourceDir, "readable.bin");
        var unreadable = Path.Combine(SourceDir, "unreadable.bin");

        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(false);
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([readable, unreadable]);
        _ = this.fileOperations.GetFileSize(readable).Returns(1_000_000L);
        _ = this.fileOperations
            .GetFileSize(unreadable)
            .Returns(_ => throw new UnauthorizedAccessException("denied"));

        _ = this.systemStorage.GetPathRoot(DestinationDir).Returns(driveRoot);
        _ = this.systemStorage.IsDriveReady(driveRoot).Returns(true);
        _ = this.systemStorage.GetAvailableFreeSpace(driveRoot).Returns(1_200_000L);

        _ = this.passwordService
            .AnalyzePasswordStrength(Arg.Any<string>())
            .Returns(new PasswordStrengthAnalysis(PasswordStrength.Weak, 20, 10, []));

        var warnings = await this.CreateSut()
            .AnalyzeWarningsAsync(ValidRequest(SourceDir, DestinationDir));

        Assert.That(
            Codes(warnings),
            Is.EqualTo(new[] { MessageCode.WeakPasswordWarning }),
            "the one measurable file needs exactly the 1,200,000 bytes the drive reports, so no space warning "
                + "is due: the unmeasurable file has to count as zero instead of aborting the estimate"
        );
    }

    [TestCase(true, TestName = "AnalyzeWarnings_DestinationDriveNotReady_SkipsTheFreeSpaceProbe")]
    [TestCase(false, TestName = "AnalyzeWarnings_DestinationRootUnknown_SkipsTheFreeSpaceProbe")]
    public async Task AnalyzeWarnings_DestinationDriveUnusable_SkipsTheFreeSpaceProbe(
        bool rootKnown
    )
    {
        var sourceFile = Path.Combine(SourceDir, "big.bin");
        var driveRoot = rootKnown ? Path.GetPathRoot(DestinationDir) : null;

        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations.DirectoryExists(DestinationDir).Returns(false);
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([sourceFile]);

        _ = this.systemStorage.GetPathRoot(DestinationDir).Returns(driveRoot);
        _ = this.systemStorage.IsDriveReady(Arg.Any<string>()).Returns(false);
        _ = this.systemStorage
            .GetAvailableFreeSpace(Arg.Any<string>())
            .Returns(_ => throw new IOException("the drive is not ready"));

        _ = this.passwordService
            .AnalyzePasswordStrength(Arg.Any<string>())
            .Returns(new PasswordStrengthAnalysis(PasswordStrength.Weak, 20, 10, []));

        var warnings = await this.CreateSut()
            .AnalyzeWarningsAsync(ValidRequest(SourceDir, DestinationDir));

        Assert.That(
            Codes(warnings),
            Is.EqualTo(new[] { MessageCode.WeakPasswordWarning }),
            "querying free space on an unknown root or an offline drive is what throws in the first place, so "
                + "the guard has to short-circuit before asking at all"
        );
    }
}
