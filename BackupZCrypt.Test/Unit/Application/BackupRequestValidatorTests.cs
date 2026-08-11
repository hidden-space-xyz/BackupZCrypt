using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Validators;
using BackupZCrypt.Application.ValueObjects.Password;
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
    /// The message the simulated I/O failure carries while the source is listed. It is a single
    /// constant because the validator has to pass it through untouched as the error's only format
    /// argument, so the thrown message and the expected argument can never drift apart.
    /// </summary>
    private const string SourceIoFailureMessage = "drive fell over";

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
    /// The format arguments the I/O arm of the source listing failure is allowed to carry: the raw
    /// exception message and nothing else.
    /// </summary>
    /// <remarks>
    /// The array lives in a field rather than inline in the case list because a constant array written
    /// straight into an argument is what CA1861 asks to be hoisted.
    /// </remarks>
    private static readonly object[] SourceIoFailureArgs = [SourceIoFailureMessage];

    /// <summary>
    /// The only containment error a destination nested inside the source may produce, hoisted out of
    /// the case list for the same CA1861 reason as <see cref="SourceIoFailureArgs"/>.
    /// </summary>
    private static readonly MessageCode[] DestinationInsideSourceOnly =
        [MessageCode.DestinationInsideSource];

    /// <summary>
    /// The only containment error a source nested inside the destination may produce, hoisted out of
    /// the case list for the same CA1861 reason as <see cref="SourceIoFailureArgs"/>.
    /// </summary>
    private static readonly MessageCode[] SourceInsideDestinationOnly =
        [MessageCode.SourceInsideDestination];

    /// <summary>
    /// The volume root containing <see cref="SourceDir"/> — <c>C:\</c> on Windows, <c>/</c> elsewhere.
    /// </summary>
    private static readonly string VolumeRoot = Path.GetPathRoot(SourceDir)!;

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
    /// Supplies the exception thrown while listing the source, the error code it must map to, and the
    /// format arguments that message is allowed to carry.
    /// </summary>
    /// <remarks>
    /// The permission-denied arm carries no arguments at all, so the raw .NET message — which embeds the
    /// full path on Windows — never reaches the user-facing dialog.
    /// </remarks>
    /// <returns>One case per catch arm of the source listing block.</returns>
    public static TheoryData<Exception, MessageCode, object[]> SourceListingFailureCases()
    {
        return new()
        {
            {
                new UnauthorizedAccessException("denied"),
                MessageCode.SourceAccessDenied,
                Array.Empty<object>()
            },
            {
                new IOException(SourceIoFailureMessage),
                MessageCode.SourceAccessErrorFormat,
                SourceIoFailureArgs
            },
        };
    }

    /// <summary>
    /// Supplies source and destination pairs together with the containment errors they must produce,
    /// including a sibling pair that shares a name prefix and must produce none.
    /// </summary>
    /// <returns>One case per containment outcome.</returns>
    public static TheoryData<string, string, MessageCode[]> OverlappingPathCases()
    {
        return new()
        {
            { SourceDir, Path.Combine(SourceDir, "backup"), DestinationInsideSourceOnly },
            { Path.Combine(DestinationDir, "data"), DestinationDir, SourceInsideDestinationOnly },
            { SourceDir, SourceDir + "-backup", Array.Empty<MessageCode>() },
            { SourceDir, VolumeRoot, SourceInsideDestinationOnly },
            { VolumeRoot, SourceDir, DestinationInsideSourceOnly },
        };
    }

    /// <summary>
    /// Creates a validator wired to the substituted file, storage, and password services.
    /// </summary>
    /// <returns>The system under test.</returns>
    private BackupRequestValidator CreateSut()
    {
        return new(this.fileOperations, this.systemStorage, this.passwordService);
    }

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
    )
    {
        return new(
            source,
            destination,
            password,
            password,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            operation
        );
    }

    /// <summary>
    /// Projects validation messages down to their codes so assertions ignore format arguments.
    /// </summary>
    /// <param name="messages">The errors or warnings returned by the validator.</param>
    /// <returns>The code of each message, in the order reported.</returns>
    private static List<MessageCode> Codes(IReadOnlyList<LocalizableMessage> messages)
    {
        return [.. messages.Select(m => m.Code)];
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

    [Fact]
    internal async Task AnalyzeErrors_EmptySourcePath_ReportsSourcePathEmpty()
    {
        var request = ValidRequest(string.Empty, DestinationDir);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(request, TestContext.Current.CancellationToken);

        Assert.Contains(MessageCode.SourcePathEmpty, Codes(errors));
    }

    [Fact]
    internal async Task AnalyzeErrors_SourceNeitherFileNorDirectory_ReportsNotExist()
    {
        _ = this.fileOperations.FileExists(SourceDir).Returns(false);
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(false);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var request = ValidRequest(SourceDir, DestinationDir);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(request, TestContext.Current.CancellationToken);

        Assert.Contains(MessageCode.SourcePathNotExistFormat, Codes(errors));
    }

    [Fact]
    internal async Task AnalyzeErrors_EmptyPassword_ReportsPasswordRequired()
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Path.Combine(SourceDir, "a.txt")]);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var request = ValidRequest(SourceDir, DestinationDir, password: string.Empty);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(request, TestContext.Current.CancellationToken);

        Assert.Contains(MessageCode.PasswordRequired, Codes(errors));
    }

    [Fact]
    internal async Task AnalyzeErrors_ShortPassword_ReportsPasswordTooShort()
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Path.Combine(SourceDir, "a.txt")]);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var request = ValidRequest(SourceDir, DestinationDir, password: "Ab1!xyz");

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(request, TestContext.Current.CancellationToken);

        Assert.Contains(MessageCode.PasswordTooShort, Codes(errors));
    }

    [Fact]
    internal async Task AnalyzeErrors_PasswordMismatch_ReportsMismatch()
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

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(request, TestContext.Current.CancellationToken);

        Assert.Contains(MessageCode.PasswordMismatch, Codes(errors));
    }

    [Fact]
    internal async Task AnalyzeErrors_SourceEqualsDestinationDirectory_ReportsSameDirectory()
    {
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Path.Combine(SourceDir, "a.txt")]);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var request = ValidRequest(SourceDir, SourceDir);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(request, TestContext.Current.CancellationToken);

        Assert.Contains(MessageCode.SourceDestinationSameDirectory, Codes(errors));
    }

    [Fact]
    internal async Task AnalyzeErrors_FullyValidRequest_ReturnsEmpty()
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

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(request, TestContext.Current.CancellationToken);

        Assert.Empty(errors);
    }

    [Fact]
    internal async Task AnalyzeWarnings_WeakPassword_ReportsWeakPasswordWarning()
    {
        _ = this.fileOperations.DirectoryExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);

        _ = this.passwordService
            .AnalyzePasswordStrength(Arg.Any<string>())
            .Returns(new PasswordStrengthAnalysis(PasswordStrength.Weak, 20, 10, []));

        var request = ValidRequest(SourceDir, DestinationDir);

        var warnings = await this.CreateSut()
            .AnalyzeWarningsAsync(request, TestContext.Current.CancellationToken);

        Assert.Contains(MessageCode.WeakPasswordWarning, Codes(warnings));
    }

    [Fact]
    internal async Task AnalyzeWarnings_StrongPassword_DoesNotReportWeakPasswordWarning()
    {
        _ = this.fileOperations.DirectoryExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);

        _ = this.passwordService
            .AnalyzePasswordStrength(Arg.Any<string>())
            .Returns(new PasswordStrengthAnalysis(PasswordStrength.Strong, 95, 110, []));

        var request = ValidRequest(SourceDir, DestinationDir);

        var warnings = await this.CreateSut()
            .AnalyzeWarningsAsync(request, TestContext.Current.CancellationToken);

        Assert.DoesNotContain(MessageCode.WeakPasswordWarning, Codes(warnings));
    }

    [Theory]
    [InlineData(PasswordStrength.VeryWeak, 10d, true)]
    [InlineData(PasswordStrength.Weak, 30d, true)]
    [InlineData(PasswordStrength.Fair, 50d, true)]
    [InlineData(PasswordStrength.Fair, 62d, true)]
    [InlineData(PasswordStrength.Good, 66d, false)]
    [InlineData(PasswordStrength.Strong, 95d, false)]
    internal async Task AnalyzeWarnings_WeakPasswordWarning_CutsAtTheGoodRatingNotAScore(
        PasswordStrength strength,
        double score,
        bool expectWarning
    )
    {
        _ = this.fileOperations.DirectoryExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);

        _ = this.passwordService
            .AnalyzePasswordStrength(Arg.Any<string>())
            .Returns(new PasswordStrengthAnalysis(strength, score, 10, []));

        var warnings = await this.CreateSut()
            .AnalyzeWarningsAsync(
                ValidRequest(SourceDir, DestinationDir),
                TestContext.Current.CancellationToken
            );

        Assert.Equal(expectWarning, Codes(warnings).Contains(MessageCode.WeakPasswordWarning));
    }

    [Fact]
    internal async Task AnalyzeWarnings_InsufficientDiskSpace_ReportsLowDiskSpace()
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

        var warnings = await this.CreateSut()
            .AnalyzeWarningsAsync(request, TestContext.Current.CancellationToken);

        Assert.Contains(MessageCode.LowDiskSpaceFormat, Codes(warnings));
    }

    [Theory]
    [InlineData(BackupOperation.Create, true)]
    [InlineData(BackupOperation.Restore, true)]
    [InlineData(BackupOperation.Update, false)]
    internal async Task AnalyzeWarnings_ExistingDestinationFiles_WarnsForCreateAndRestoreOnly(
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

        var warnings = await this.CreateSut()
            .AnalyzeWarningsAsync(request, TestContext.Current.CancellationToken);

        var hasWarning = Codes(warnings).Contains(MessageCode.DestinationExistingFilesFormat);
        Assert.Equal(expectedWarning, hasWarning);
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

    [Fact]
    internal async Task AnalyzeErrors_SourceIsAFile_ReportsMustBeDirectoryWithoutListingIt()
    {
        _ = this.fileOperations.FileExists(SourceDir).Returns(true);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(
                ValidRequest(SourceDir, DestinationDir),
                TestContext.Current.CancellationToken
            );

        Assert.Multiple(
            () => Assert.Contains(MessageCode.SourceMustBeDirectory, Codes(errors)),
            () => Assert.DoesNotContain(MessageCode.SourcePathNotExistFormat, Codes(errors))
        );

        await this.fileOperations.DidNotReceive()
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    internal async Task AnalyzeErrors_SourceDirectoryHasNoFiles_ReportsSourceDirectoryEmpty()
    {
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(
                ValidRequest(SourceDir, DestinationDir),
                TestContext.Current.CancellationToken
            );

        MessageCode[] expectedCodes = [MessageCode.SourceDirectoryEmpty];
        Assert.Equal(expectedCodes, Codes(errors));
    }

    [Theory]
    [MemberData(nameof(SourceListingFailureCases))]
    internal async Task AnalyzeErrors_SourceListingThrows_ReportsTheMatchingAccessError(
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
            .AnalyzeErrorsAsync(
                ValidRequest(SourceDir, DestinationDir),
                TestContext.Current.CancellationToken
            );

        MessageCode[] expectedCodes = [expectedCode];
        Assert.Multiple(
            () => Assert.Equal(expectedCodes, Codes(errors)),
            () => Assert.Equal(expectedArgs, errors[0].Args)
        );
    }

    [Fact]
    internal async Task AnalyzeErrors_EmptyDestinationPath_ReportsDestinationPathEmptyWithoutProbing()
    {
        this.StubReadableSource();

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(
                ValidRequest(SourceDir, string.Empty),
                TestContext.Current.CancellationToken
            );

        MessageCode[] expectedCodes = [MessageCode.DestinationPathEmpty];
        Assert.Equal(expectedCodes, Codes(errors));

        _ = this.systemStorage.DidNotReceive().GetPathRoot(Arg.Any<string>());
    }

    [Fact]
    internal async Task AnalyzeErrors_DestinationDriveNotReady_ReportsDriveNotAccessibleWithTheRoot()
    {
        var driveRoot = Path.GetPathRoot(DestinationDir)!;

        this.StubReadableSource();
        _ = this.systemStorage.GetPathRoot(DestinationDir).Returns(driveRoot);
        _ = this.systemStorage.IsDriveReady(driveRoot).Returns(false);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(
                ValidRequest(SourceDir, DestinationDir),
                TestContext.Current.CancellationToken
            );

        MessageCode[] expectedCodes = [MessageCode.DestinationDriveNotAccessibleFormat];
        Assert.Multiple(
            () => Assert.Equal(expectedCodes, Codes(errors)),
            () => Assert.Equal(new object[] { driveRoot }, errors[0].Args)
        );
    }

    [Fact]
    internal async Task AnalyzeErrors_DestinationRootUnknown_ReportsNothingAndSkipsTheReadinessProbe()
    {
        this.StubReadableSource();
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns((string?)null);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(
                ValidRequest(SourceDir, DestinationDir),
                TestContext.Current.CancellationToken
            );

        Assert.Empty(errors);

        _ = this.systemStorage.DidNotReceive().IsDriveReady(Arg.Any<string>());
    }

    [Fact]
    internal async Task AnalyzeErrors_DestinationRootLookupThrows_ReportsDestinationInvalidWithMessage()
    {
        static ArgumentException RootLookupFailure(string fullPath)
        {
            return new ArgumentException($"bad root: {fullPath}", nameof(fullPath));
        }

        var lookupFailure = RootLookupFailure(DestinationDir);

        this.StubReadableSource();
        _ = this.systemStorage.GetPathRoot(DestinationDir).Returns(_ => throw lookupFailure);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(
                ValidRequest(SourceDir, DestinationDir),
                TestContext.Current.CancellationToken
            );

        MessageCode[] expectedCodes = [MessageCode.DestinationInvalidFormat];
        Assert.Multiple(
            () => Assert.Equal(expectedCodes, Codes(errors)),
            () => Assert.Equal(new object[] { lookupFailure.Message }, errors[0].Args)
        );
    }

    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 2)]
    internal async Task AnalyzeErrors_UnnormalizablePath_ReportsOneInvalidPathFormatPerBadPath(
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

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(request, TestContext.Current.CancellationToken);

        Assert.Multiple(
            () => Assert.Equal(expectedCount, errors.Count),
            () =>
                Assert.All(
                    Codes(errors),
                    code => Assert.Equal(MessageCode.InvalidPathFormat, code)
                ),
            () => Assert.Single(errors[0].Args)
        );

        _ = this.fileOperations.DidNotReceive().DirectoryExists(Arg.Any<string>());
        _ = this.fileOperations.DidNotReceive().FileExists(Arg.Any<string>());
    }

    [Theory]
    [InlineData(1000, false)]
    [InlineData(1001, true)]
    internal async Task AnalyzeErrors_PasswordLength_ReportsTooLongOnlyBeyondTheLimit(
        int length,
        bool expectTooLong
    )
    {
        this.StubReadableSource();
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var password = new string('x', length);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(
                ValidRequest(SourceDir, DestinationDir, password: password),
                TestContext.Current.CancellationToken
            );

        Assert.Multiple(
            () => Assert.Equal(expectTooLong, Codes(errors).Contains(MessageCode.PasswordTooLong)),
            () => Assert.DoesNotContain(MessageCode.PasswordTooShort, Codes(errors))
        );
    }

    [Theory]
    [InlineData(" Str0ng-Passw0rd!", true)]
    [InlineData("Str0ng-Passw0rd! ", true)]
    [InlineData("\tStr0ng-Passw0rd!", true)]
    [InlineData("Str0ng Passw0rd!", false)]
    internal async Task AnalyzeErrors_Password_RejectsOnlySurroundingWhitespace(
        string password,
        bool expectRejected
    )
    {
        this.StubReadableSource();
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(
                ValidRequest(SourceDir, DestinationDir, password: password),
                TestContext.Current.CancellationToken
            );

        Assert.Multiple(
            () =>
                Assert.Equal(
                    expectRejected,
                    Codes(errors).Contains(MessageCode.PasswordLeadingTrailingSpaces)
                ),
            () => Assert.DoesNotContain(MessageCode.PasswordMismatch, Codes(errors))
        );
    }

    [Theory]
    [InlineData(BackupOperation.Create, "", MessageCode.ConfirmPasswordRequired, true)]
    [InlineData(BackupOperation.Create, "Different-Passw0rd!", MessageCode.PasswordMismatch, true)]
    [InlineData(BackupOperation.Restore, "", MessageCode.ConfirmPasswordRequired, false)]
    [InlineData(BackupOperation.Update, "Different-Passw0rd!", MessageCode.PasswordMismatch, false)]
    internal async Task AnalyzeErrors_ConfirmPassword_IsEnforcedOnCreateOnly(
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

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(expectReported, Codes(errors).Contains(expectedCode));
    }

    [Theory]
    [MemberData(nameof(OverlappingPathCases))]
    internal async Task AnalyzeErrors_SourceAndDestinationOverlap_ReportsTheMatchingContainmentError(
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

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(
                ValidRequest(source, destination),
                TestContext.Current.CancellationToken
            );

        var containment = Codes(errors)
            .Where(c =>
                c is MessageCode.SourceDestinationSameDirectory
                    or MessageCode.DestinationInsideSource
                    or MessageCode.SourceInsideDestination
            )
            .ToList();

        Assert.Equal(expectedCodes, containment);
    }

    [Fact]
    internal async Task AnalyzeErrors_OverlapProbeThrows_ReportsNoContainmentErrorAtAll()
    {
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations
            .DirectoryExists(SourceDir)
            .Returns(_ => true, _ => throw new IOException("probe failed"));
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Path.Combine(SourceDir, "a.txt")]);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(
                ValidRequest(SourceDir, SourceDir),
                TestContext.Current.CancellationToken
            );

        Assert.Empty(errors);
    }

    [Fact]
    internal async Task AnalyzeErrors_DestinationDiffersFromSourceByCaseOnly_FollowsThePlatformRule()
    {
        _ = this.fileOperations.FileExists(Arg.Any<string>()).Returns(false);
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns([Path.Combine(SourceDir, "a.txt")]);
        _ = this.systemStorage.GetPathRoot(Arg.Any<string>()).Returns(string.Empty);

        var errors = await this.CreateSut()
            .AnalyzeErrorsAsync(
                ValidRequest(SourceDir, SourceDir.ToUpperInvariant()),
                TestContext.Current.CancellationToken
            );

        MessageCode[] expected = OperatingSystem.IsWindows()
            ? [MessageCode.SourceDestinationSameDirectory]
            : [];

        Assert.Equal(expected, Codes(errors));
    }

    [Fact]
    internal async Task AnalyzeWarnings_SourceListingThrows_ReturnsEmptyInsteadOfPropagating()
    {
        _ = this.fileOperations.DirectoryExists(SourceDir).Returns(true);
        _ = this.fileOperations
            .GetFilesAsync(SourceDir, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("gone"));

        var warnings = await this.CreateSut()
            .AnalyzeWarningsAsync(
                ValidRequest(SourceDir, DestinationDir),
                TestContext.Current.CancellationToken
            );

        Assert.Empty(warnings);
    }

    [Theory]
    [InlineData(-1L, false)]
    [InlineData(1_200_000L, false)]
    [InlineData(1_199_999L, true)]
    internal async Task AnalyzeWarnings_FreeSpace_WarnsOnlyWhenAKnownAmountFallsShort(
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
            .AnalyzeWarningsAsync(
                ValidRequest(SourceDir, DestinationDir),
                TestContext.Current.CancellationToken
            );

        Assert.Equal(expectWarning, Codes(warnings).Contains(MessageCode.LowDiskSpaceFormat));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    internal async Task AnalyzeWarnings_UnnormalizablePath_ReturnsEmptyWithoutTouchingTheFileSystem(
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

        var warnings = await this.CreateSut()
            .AnalyzeWarningsAsync(request, TestContext.Current.CancellationToken);

        Assert.Empty(warnings);

        _ = this.fileOperations.DidNotReceive().DirectoryExists(Arg.Any<string>());
        _ = this.passwordService.DidNotReceive().AnalyzePasswordStrength(Arg.Any<string>());
    }

    [Fact]
    internal async Task AnalyzeWarnings_FileSizeProbeThrows_CountsThatFileAsZeroAndKeepsSweeping()
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
            .AnalyzeWarningsAsync(
                ValidRequest(SourceDir, DestinationDir),
                TestContext.Current.CancellationToken
            );

        MessageCode[] expectedCodes = [MessageCode.WeakPasswordWarning];
        Assert.Equal(expectedCodes, Codes(warnings));
    }

    [Theory]
    [InlineData(
        true,
        TestDisplayName = "AnalyzeWarnings_DestinationDriveNotReady_SkipsTheFreeSpaceProbe"
    )]
    [InlineData(
        false,
        TestDisplayName = "AnalyzeWarnings_DestinationRootUnknown_SkipsTheFreeSpaceProbe"
    )]
    internal async Task AnalyzeWarnings_DestinationDriveUnusable_SkipsTheFreeSpaceProbe(
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
            .AnalyzeWarningsAsync(
                ValidRequest(SourceDir, DestinationDir),
                TestContext.Current.CancellationToken
            );

        MessageCode[] expectedCodes = [MessageCode.WeakPasswordWarning];
        Assert.Equal(expectedCodes, Codes(warnings));
    }
}
