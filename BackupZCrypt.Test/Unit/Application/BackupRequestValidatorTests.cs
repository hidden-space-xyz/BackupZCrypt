using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Password;
using BackupZCrypt.Application.Validators;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Application;

/// <summary>
/// Unit tests for the backup request validator's blocking errors and advisory warnings.
/// </summary>
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
}
