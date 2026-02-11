using CloudZCrypt.Application.Services.Interfaces;
using CloudZCrypt.Application.Validators;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Services.Interfaces;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;
using NSubstitute;

namespace CloudZCrypt.Test.Application.Validators;

[TestFixture]
internal sealed class FileCryptRequestValidatorTests
{
    private IFileOperationsService _fileOps = null!;
    private ISystemStorageService _storage = null!;
    private IPasswordService _passwordService = null!;
    private FileCryptRequestValidator _validator = null!;

    [SetUp]
    public void SetUp()
    {
        _fileOps = Substitute.For<IFileOperationsService>();
        _storage = Substitute.For<ISystemStorageService>();
        _passwordService = Substitute.For<IPasswordService>();
        _validator = new FileCryptRequestValidator(_fileOps, _storage, _passwordService);
    }

    private static FileCryptRequest CreateRequest(
        string source = @"C:\source\file.txt",
        string dest = @"C:\dest\file.czc",
        string password = "StrongP@ss1",
        string confirmPassword = "StrongP@ss1",
        EncryptOperation operation = EncryptOperation.Encrypt
    ) =>
        new(
            source,
            dest,
            password,
            confirmPassword,
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            operation,
            NameObfuscationMode.None
        );

    [Test]
    public async Task AnalyzeErrors_EmptyPassword_ReturnsError()
    {
        FileCryptRequest request = CreateRequest(password: "", confirmPassword: "");

        IReadOnlyList<string> errors = await _validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Has.Some.Contains("password"));
    }

    [Test]
    public async Task AnalyzeErrors_ShortPassword_ReturnsError()
    {
        FileCryptRequest request = CreateRequest(password: "short", confirmPassword: "short");
        _fileOps.FileExists(Arg.Any<string>()).Returns(true);
        _fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        _fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        _storage.GetPathRoot(Arg.Any<string>()).Returns(@"C:\");
        _storage.IsDriveReady(Arg.Any<string>()).Returns(true);

        IReadOnlyList<string> errors = await _validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Has.Some.Contains("at least 8 characters"));
    }

    [Test]
    public async Task AnalyzeErrors_PasswordMismatch_ReturnsError()
    {
        FileCryptRequest request = CreateRequest(
            password: "StrongP@ss1",
            confirmPassword: "DifferentPass1"
        );
        _fileOps.FileExists(Arg.Any<string>()).Returns(true);
        _fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        _fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        _storage.GetPathRoot(Arg.Any<string>()).Returns(@"C:\");
        _storage.IsDriveReady(Arg.Any<string>()).Returns(true);

        IReadOnlyList<string> errors = await _validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Has.Some.Contains("do not match"));
    }

    [Test]
    public async Task AnalyzeErrors_PasswordWithLeadingSpaces_ReturnsError()
    {
        FileCryptRequest request = CreateRequest(
            password: " StrongP@ss1",
            confirmPassword: " StrongP@ss1"
        );
        _fileOps.FileExists(Arg.Any<string>()).Returns(true);
        _fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        _fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        _storage.GetPathRoot(Arg.Any<string>()).Returns(@"C:\");
        _storage.IsDriveReady(Arg.Any<string>()).Returns(true);

        IReadOnlyList<string> errors = await _validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Has.Some.Contains("spaces"));
    }

    [Test]
    public async Task AnalyzeErrors_PasswordTooLong_ReturnsError()
    {
        string longPassword = new('A', 1001);
        FileCryptRequest request = CreateRequest(
            password: longPassword,
            confirmPassword: longPassword
        );
        _fileOps.FileExists(Arg.Any<string>()).Returns(true);
        _fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        _fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        _storage.GetPathRoot(Arg.Any<string>()).Returns(@"C:\");
        _storage.IsDriveReady(Arg.Any<string>()).Returns(true);

        IReadOnlyList<string> errors = await _validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Has.Some.Contains("too long"));
    }

    [Test]
    public async Task AnalyzeErrors_SourceDoesNotExist_ReturnsError()
    {
        _fileOps.FileExists(Arg.Any<string>()).Returns(false);
        _fileOps.DirectoryExists(Arg.Any<string>()).Returns(false);
        _storage.GetPathRoot(Arg.Any<string>()).Returns(@"C:\");
        _storage.IsDriveReady(Arg.Any<string>()).Returns(true);

        FileCryptRequest request = CreateRequest();

        IReadOnlyList<string> errors = await _validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Has.Some.Contains("does not exist"));
    }

    [Test]
    public async Task AnalyzeErrors_EmptyFile_ReturnsError()
    {
        _fileOps.FileExists(Arg.Any<string>()).Returns(true);
        _fileOps.GetFileSize(Arg.Any<string>()).Returns(0L);
        _fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        _storage.GetPathRoot(Arg.Any<string>()).Returns(@"C:\");
        _storage.IsDriveReady(Arg.Any<string>()).Returns(true);

        FileCryptRequest request = CreateRequest();

        IReadOnlyList<string> errors = await _validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Has.Some.Contains("empty"));
    }

    [Test]
    public async Task AnalyzeErrors_EmptyDirectory_ReturnsError()
    {
        _fileOps.FileExists(Arg.Any<string>()).Returns(false);
        _fileOps.DirectoryExists(Arg.Any<string>()).Returns(true);
        _fileOps
            .GetFilesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<string>());
        _storage.GetPathRoot(Arg.Any<string>()).Returns(@"C:\");
        _storage.IsDriveReady(Arg.Any<string>()).Returns(true);

        FileCryptRequest request = CreateRequest(source: @"C:\source\dir", dest: @"C:\dest\dir");

        IReadOnlyList<string> errors = await _validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Has.Some.Contains("empty"));
    }

    [Test]
    public async Task AnalyzeErrors_SameSourceAndDestFile_ReturnsError()
    {
        string path = Path.GetFullPath(@"C:\same\file.txt");
        _fileOps.FileExists(Arg.Any<string>()).Returns(true);
        _fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        _fileOps.GetDirectoryName(Arg.Any<string>()).Returns(Path.GetDirectoryName(path));
        _storage.GetPathRoot(Arg.Any<string>()).Returns(@"C:\");
        _storage.IsDriveReady(Arg.Any<string>()).Returns(true);

        FileCryptRequest request = CreateRequest(source: path, dest: path);

        IReadOnlyList<string> errors = await _validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Has.Some.Contains("cannot be the same"));
    }

    [Test]
    public async Task AnalyzeErrors_EmptyDestination_ReturnsError()
    {
        _fileOps.FileExists(Arg.Any<string>()).Returns(true);
        _fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);

        FileCryptRequest request = CreateRequest(dest: "");

        IReadOnlyList<string> errors = await _validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Has.Some.Contains("destination"));
    }

    [Test]
    public async Task AnalyzeErrors_ValidRequest_ReturnsNoErrors()
    {
        _fileOps.FileExists(Arg.Any<string>()).Returns(true);
        _fileOps.GetFileSize(Arg.Any<string>()).Returns(100L);
        _fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");
        _storage.GetPathRoot(Arg.Any<string>()).Returns(@"C:\");
        _storage.IsDriveReady(Arg.Any<string>()).Returns(true);

        FileCryptRequest request = CreateRequest();

        IReadOnlyList<string> errors = await _validator.AnalyzeErrorsAsync(request);

        Assert.That(errors, Is.Empty);
    }
}
