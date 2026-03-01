namespace BackupZCrypt.Test.Application.Orchestrators;

using BackupZCrypt.Application.Orchestrators;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Validators.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.FileCrypt;
using NSubstitute;

[TestFixture]
internal sealed class FileCryptOrchestratorTests
{
    private IFileCryptDirectoryService directoryService = null!;
    private IFileOperationsService fileOps = null!;
    private FileCryptOrchestrator orchestrator = null!;
    private IProgress<FileCryptStatus> progress = null!;
    private IFileCryptSingleFileService singleFileService = null!;
    private IFileCryptRequestValidator validator = null!;

    [Test]
    public void Execute_Cancellation_ThrowsOperationCanceled()
    {
        this.validator
            .AnalyzeErrorsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        this.validator
            .AnalyzeWarningsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        this.fileOps.FileExists(Arg.Any<string>()).Returns(true);
        this.fileOps.DirectoryExists(Arg.Any<string>()).Returns(false);
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");

        this.singleFileService
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<FileCryptRequest>(),
                Arg.Any<IProgress<FileCryptStatus>>(),
                Arg.Any<CancellationToken>())
            .Returns<Result<FileCryptResult>>(_ => throw new OperationCanceledException());

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await orchestrator.ExecuteAsync(CreateRequest(), progress));
    }

    [Test]
    public async Task Execute_DirectorySource_DelegatesToDirectoryService()
    {
        this.validator
            .AnalyzeErrorsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        this.validator
            .AnalyzeWarningsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        this.fileOps.DirectoryExists(Arg.Any<string>()).Returns(true);
        this.fileOps.FileExists(Arg.Any<string>()).Returns(false);

        FileCryptResult expected = new(true, TimeSpan.FromSeconds(1), 500, 5, 5);
        this.directoryService
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<FileCryptRequest>(),
                Arg.Any<IProgress<FileCryptStatus>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<FileCryptResult>.Success(expected));

        Result<FileCryptResult> result = await orchestrator.ExecuteAsync(
            CreateRequest(),
            progress);

        Assert.That(result.Value.IsSuccess, Is.True);
        await directoryService
            .Received(1)
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<FileCryptRequest>(),
                Arg.Any<IProgress<FileCryptStatus>>(),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_FileSource_DelegatesToSingleFileService()
    {
        this.validator
            .AnalyzeErrorsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        this.validator
            .AnalyzeWarningsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        this.fileOps.DirectoryExists(Arg.Any<string>()).Returns(false);
        this.fileOps.FileExists(Arg.Any<string>()).Returns(true);
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");

        FileCryptResult expected = new(true, TimeSpan.FromSeconds(1), 100, 1, 1);
        this.singleFileService
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<FileCryptRequest>(),
                Arg.Any<IProgress<FileCryptStatus>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<FileCryptResult>.Success(expected));

        Result<FileCryptResult> result = await orchestrator.ExecuteAsync(
            CreateRequest(),
            progress);

        Assert.That(result.Value.IsSuccess, Is.True);
        await singleFileService
            .Received(1)
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<FileCryptRequest>(),
                Arg.Any<IProgress<FileCryptStatus>>(),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_SourceDoesNotExist_ReturnsFailure()
    {
        this.validator
            .AnalyzeErrorsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        this.validator
            .AnalyzeWarningsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        this.fileOps.FileExists(Arg.Any<string>()).Returns(false);
        this.fileOps.DirectoryExists(Arg.Any<string>()).Returns(false);

        Result<FileCryptResult> result = await orchestrator.ExecuteAsync(
            CreateRequest(),
            progress);

        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public async Task Execute_UnexpectedException_ReturnsFailure()
    {
        this.validator
            .AnalyzeErrorsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        this.validator
            .AnalyzeWarningsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        this.fileOps.FileExists(Arg.Any<string>()).Returns(true);
        this.fileOps.DirectoryExists(Arg.Any<string>()).Returns(false);
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");

        this.singleFileService
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<FileCryptRequest>(),
                Arg.Any<IProgress<FileCryptStatus>>(),
                Arg.Any<CancellationToken>())
            .Returns<Result<FileCryptResult>>(_ => throw new InvalidOperationException("boom"));

        Result<FileCryptResult> result = await orchestrator.ExecuteAsync(
            CreateRequest(),
            progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Errors, Has.Some.Contains("unexpected"));
        }
    }

    [Test]
    public async Task Execute_WithValidationErrors_ReturnsResultWithErrors()
    {
        this.validator
            .AnalyzeErrorsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string> { "error1" });

        Result<FileCryptResult> result = await orchestrator.ExecuteAsync(
            CreateRequest(),
            progress);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Errors, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task Execute_WithWarningsAndNotProceeding_ReturnsWarnings()
    {
        this.validator
            .AnalyzeErrorsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        this.validator
            .AnalyzeWarningsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string> { "warning1" });

        Result<FileCryptResult> result = await orchestrator.ExecuteAsync(
            CreateRequest(),
            progress);

        Assert.That(result.Value.HasWarnings, Is.True);
    }

    [Test]
    public async Task Execute_WithWarningsAndProceedOnWarnings_ProcessesFile()
    {
        FileCryptRequest request = CreateRequest() with { ProceedOnWarnings = true };

        this.validator
            .AnalyzeErrorsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        this.validator
            .AnalyzeWarningsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string> { "warning1" });

        this.fileOps.FileExists(Arg.Any<string>()).Returns(true);
        this.fileOps.DirectoryExists(Arg.Any<string>()).Returns(false);
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");

        FileCryptResult expected = new(true, TimeSpan.FromSeconds(1), 100, 1, 1);
        this.singleFileService
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<FileCryptRequest>(),
                Arg.Any<IProgress<FileCryptStatus>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<FileCryptResult>.Success(expected));

        Result<FileCryptResult> result = await orchestrator.ExecuteAsync(request, progress);

        Assert.That(result.Value.IsSuccess, Is.True);
    }

    [SetUp]
    public void SetUp()
    {
        this.validator = Substitute.For<IFileCryptRequestValidator>();
        this.fileOps = Substitute.For<IFileOperationsService>();
        this.singleFileService = Substitute.For<IFileCryptSingleFileService>();
        this.directoryService = Substitute.For<IFileCryptDirectoryService>();
        this.progress = Substitute.For<IProgress<FileCryptStatus>>();

        this.orchestrator = new FileCryptOrchestrator(
            this.validator,
            this.fileOps,
            this.singleFileService,
            this.directoryService);
    }

    private static FileCryptRequest CreateRequest() =>
        new(
            @"C:\source\file.txt",
            @"C:\dest\file.bzc",
            "StrongP@ss1",
            "StrongP@ss1",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            EncryptOperation.Encrypt,
            NameObfuscationMode.None);
}
