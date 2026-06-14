using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Password;
using BackupZCrypt.Application.Validators;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Application;

public sealed class BackupRequestValidatorTests
{
    private static readonly string SourceDir = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "bzc-validator-src")
    );
    private static readonly string DestinationDir = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "bzc-validator-dst")
    );

    private readonly IFileOperationsService fileOperations =
        Substitute.For<IFileOperationsService>();
    private readonly ISystemStorageService systemStorage = Substitute.For<ISystemStorageService>();
    private readonly IPasswordService passwordService = Substitute.For<IPasswordService>();

    private BackupRequestValidator CreateSut() =>
        new(this.fileOperations, this.systemStorage, this.passwordService);

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
