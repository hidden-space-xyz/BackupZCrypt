namespace BackupZCrypt.Test.Application.Validators;

using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Validators;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using NSubstitute;

[TestFixture]
internal sealed class BackupRequestValidatorTests
{
    private IFileOperationsService fileOps = null!;
    private IPasswordService passwordService = null!;
    private ISystemStorageService storage = null!;
    private BackupRequestValidator validator = null!;

    [Test]
    public async Task AnalyzeErrors_EmptyDestination_ReturnsError()
    {
        this.fileOps.FileExists(Arg.Any<string>()).Returns(true);
        this.fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);

        BackupRequest request = CreateRequest(dest: string.Empty);

        IReadOnlyList<string> errors = await validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Has.Some.Contains("destination"));
    }

    [Test]
    public async Task AnalyzeErrors_EmptyDirectory_ReturnsError()
    {
        this.fileOps.FileExists(Arg.Any<string>()).Returns(false);
        this.fileOps.DirectoryExists(Arg.Any<string>()).Returns(true);
        this.fileOps
            .GetFilesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        this.storage.GetPathRoot(Arg.Any<string>()).Returns(@"C:\");
        this.storage.IsDriveReady(Arg.Any<string>()).Returns(true);

        BackupRequest request = CreateRequest(source: @"C:\source\dir", dest: @"C:\dest\dir");

        IReadOnlyList<string> errors = await validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Has.Some.Contains("empty"));
    }

    [Test]
    public async Task AnalyzeErrors_EmptyFile_ReturnsError()
    {
        this.fileOps.FileExists(Arg.Any<string>()).Returns(true);
        this.fileOps.GetFileSize(Arg.Any<string>()).Returns(0L);
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        this.storage.GetPathRoot(Arg.Any<string>()).Returns(@"C:\");
        this.storage.IsDriveReady(Arg.Any<string>()).Returns(true);

        BackupRequest request = CreateRequest();

        IReadOnlyList<string> errors = await validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Has.Some.Contains("empty"));
    }

    [Test]
    public async Task AnalyzeErrors_EmptyPassword_ReturnsError()
    {
        BackupRequest request = CreateRequest(password: string.Empty, confirmPassword: string.Empty);

        IReadOnlyList<string> errors = await validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Has.Some.Contains("password"));
    }

    [Test]
    public async Task AnalyzeErrors_PasswordMismatch_ReturnsError()
    {
        BackupRequest request = CreateRequest(
            password: "StrongP@ss1",
            confirmPassword: "DifferentPass1");
        this.fileOps.FileExists(Arg.Any<string>()).Returns(true);
        this.fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        this.storage.GetPathRoot(Arg.Any<string>()).Returns(@"C:\");
        this.storage.IsDriveReady(Arg.Any<string>()).Returns(true);

        IReadOnlyList<string> errors = await validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Has.Some.Contains("do not match"));
    }

    [Test]
    public async Task AnalyzeErrors_PasswordTooLong_ReturnsError()
    {
        string longPassword = new('A', 1001);
        BackupRequest request = CreateRequest(
            password: longPassword,
            confirmPassword: longPassword);
        this.fileOps.FileExists(Arg.Any<string>()).Returns(true);
        this.fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        this.storage.GetPathRoot(Arg.Any<string>()).Returns(@"C:\");
        this.storage.IsDriveReady(Arg.Any<string>()).Returns(true);

        IReadOnlyList<string> errors = await validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Has.Some.Contains("too long"));
    }

    [Test]
    public async Task AnalyzeErrors_PasswordWithLeadingSpaces_ReturnsError()
    {
        BackupRequest request = CreateRequest(
            password: " StrongP@ss1",
            confirmPassword: " StrongP@ss1");
        this.fileOps.FileExists(Arg.Any<string>()).Returns(true);
        this.fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        this.storage.GetPathRoot(Arg.Any<string>()).Returns(@"C:\");
        this.storage.IsDriveReady(Arg.Any<string>()).Returns(true);

        IReadOnlyList<string> errors = await validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Has.Some.Contains("spaces"));
    }

    [Test]
    public async Task AnalyzeErrors_SameSourceAndDestFile_ReturnsError()
    {
        string path = Path.GetFullPath(@"C:\same\file.txt");
        this.fileOps.FileExists(Arg.Any<string>()).Returns(true);
        this.fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(Path.GetDirectoryName(path));
        this.storage.GetPathRoot(Arg.Any<string>()).Returns(@"C:\");
        this.storage.IsDriveReady(Arg.Any<string>()).Returns(true);

        BackupRequest request = CreateRequest(source: path, dest: path);

        IReadOnlyList<string> errors = await validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Has.Some.Contains("cannot be the same"));
    }

    [Test]
    public async Task AnalyzeErrors_ShortPassword_ReturnsError()
    {
        BackupRequest request = CreateRequest(password: "short", confirmPassword: "short");
        this.fileOps.FileExists(Arg.Any<string>()).Returns(true);
        this.fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        this.storage.GetPathRoot(Arg.Any<string>()).Returns(@"C:\");
        this.storage.IsDriveReady(Arg.Any<string>()).Returns(true);

        IReadOnlyList<string> errors = await validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Has.Some.Contains("at least 8 characters"));
    }

    [Test]
    public async Task AnalyzeErrors_SourceDoesNotExist_ReturnsError()
    {
        this.fileOps.FileExists(Arg.Any<string>()).Returns(false);
        this.fileOps.DirectoryExists(Arg.Any<string>()).Returns(false);
        this.storage.GetPathRoot(Arg.Any<string>()).Returns(@"C:\");
        this.storage.IsDriveReady(Arg.Any<string>()).Returns(true);

        BackupRequest request = CreateRequest();

        IReadOnlyList<string> errors = await validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Has.Some.Contains("does not exist"));
    }

    [Test]
    public async Task AnalyzeErrors_ValidRequest_ReturnsNoErrors()
    {
        this.fileOps.FileExists(Arg.Any<string>()).Returns(true);
        this.fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        this.storage.GetPathRoot(Arg.Any<string>()).Returns(@"C:\");
        this.storage.IsDriveReady(Arg.Any<string>()).Returns(true);

        BackupRequest request = CreateRequest();

        IReadOnlyList<string> errors = await validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Is.Empty);
    }

    [SetUp]
    public void SetUp()
    {
        this.fileOps = Substitute.For<IFileOperationsService>();
        this.storage = Substitute.For<ISystemStorageService>();
        this.passwordService = Substitute.For<IPasswordService>();
        this.validator = new BackupRequestValidator(this.fileOps, this.storage, this.passwordService);
    }

    private static BackupRequest CreateRequest(
        string source = @"C:\source\file.txt",
        string dest = @"C:\dest\file.bzc",
        string password = "StrongP@ss1",
        string confirmPassword = "StrongP@ss1",
        EncryptOperation operation = EncryptOperation.Encrypt) =>
        new(
            source,
            dest,
            password,
            confirmPassword,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            operation,
            NameObfuscationMode.None);
}
