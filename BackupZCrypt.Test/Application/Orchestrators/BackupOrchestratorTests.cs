namespace BackupZCrypt.Test.Application.Orchestrators;

using BackupZCrypt.Application.Orchestrators;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Validators.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using NSubstitute;

[TestFixture]
internal sealed class BackupOrchestratorTests
{
    private IDirectoryBackupService directoryService = null!;
    private IFileOperationsService fileOps = null!;
    private BackupOrchestrator orchestrator = null!;
    private IProgress<BackupStatus> progress = null!;
    private ISingleFileBackupService singleFileService = null!;
    private IBackupRequestValidator validator = null!;

    [Test]
    public void Execute_Cancellation_ThrowsOperationCanceled()
    {
        this.validator
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        this.validator
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        this.fileOps.FileExists(Arg.Any<string>()).Returns(true);
        this.fileOps.DirectoryExists(Arg.Any<string>()).Returns(false);
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");

        this.singleFileService
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>())
            .Returns<Result<BackupResult>>(_ => throw new OperationCanceledException());

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await orchestrator.ExecuteAsync(CreateRequest(), progress));
    }

    [Test]
    public async Task Execute_DirectorySource_DelegatesToDirectoryService()
    {
        this.validator
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        this.validator
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        this.fileOps.DirectoryExists(Arg.Any<string>()).Returns(true);
        this.fileOps.FileExists(Arg.Any<string>()).Returns(false);

        BackupResult expected = new(true, TimeSpan.FromSeconds(1), 500, 5, 5);
        this.directoryService
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<BackupResult>.Success(expected));

        Result<BackupResult> result = await orchestrator.ExecuteAsync(
            CreateRequest(),
            progress);

        Assert.That(result.Value.IsSuccess, Is.True);
        await directoryService
            .Received(1)
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_FileSource_DelegatesToSingleFileService()
    {
        this.validator
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        this.validator
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        this.fileOps.DirectoryExists(Arg.Any<string>()).Returns(false);
        this.fileOps.FileExists(Arg.Any<string>()).Returns(true);
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");

        BackupResult expected = new(true, TimeSpan.FromSeconds(1), 100, 1, 1);
        this.singleFileService
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<BackupResult>.Success(expected));

        Result<BackupResult> result = await orchestrator.ExecuteAsync(
            CreateRequest(),
            progress);

        Assert.That(result.Value.IsSuccess, Is.True);
        await singleFileService
            .Received(1)
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_SourceDoesNotExist_ReturnsFailure()
    {
        this.validator
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        this.validator
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        this.fileOps.FileExists(Arg.Any<string>()).Returns(false);
        this.fileOps.DirectoryExists(Arg.Any<string>()).Returns(false);

        Result<BackupResult> result = await orchestrator.ExecuteAsync(
            CreateRequest(),
            progress);

        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public async Task Execute_UnexpectedException_ReturnsFailure()
    {
        this.validator
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        this.validator
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        this.fileOps.FileExists(Arg.Any<string>()).Returns(true);
        this.fileOps.DirectoryExists(Arg.Any<string>()).Returns(false);
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");

        this.singleFileService
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>())
            .Returns<Result<BackupResult>>(_ => throw new InvalidOperationException("boom"));

        Result<BackupResult> result = await orchestrator.ExecuteAsync(
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
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string> { "error1" });

        Result<BackupResult> result = await orchestrator.ExecuteAsync(
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
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        this.validator
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string> { "warning1" });

        Result<BackupResult> result = await orchestrator.ExecuteAsync(
            CreateRequest(),
            progress);

        Assert.That(result.Value.HasWarnings, Is.True);
    }

    [Test]
    public async Task Execute_WithWarningsAndProceedOnWarnings_ProcessesFile()
    {
        BackupRequest request = CreateRequest() with { ProceedOnWarnings = true };

        this.validator
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        this.validator
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string> { "warning1" });

        this.fileOps.FileExists(Arg.Any<string>()).Returns(true);
        this.fileOps.DirectoryExists(Arg.Any<string>()).Returns(false);
        this.fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");

        BackupResult expected = new(true, TimeSpan.FromSeconds(1), 100, 1, 1);
        this.singleFileService
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<BackupResult>.Success(expected));

        Result<BackupResult> result = await orchestrator.ExecuteAsync(request, progress);

        Assert.That(result.Value.IsSuccess, Is.True);
    }

    [Test]
    public async Task Execute_UpdateOperation_FileSource_ReturnsFailure()
    {
        BackupRequest request = CreateRequest() with { Operation = EncryptOperation.Update };

        this.validator
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        this.validator
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        this.fileOps.FileExists(Arg.Any<string>()).Returns(true);
        this.fileOps.DirectoryExists(Arg.Any<string>()).Returns(false);

        Result<BackupResult> result = await orchestrator.ExecuteAsync(request, progress);

        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public async Task Execute_UpdateOperation_DirectorySource_DelegatesToDirectoryService()
    {
        BackupRequest request = CreateRequest() with { Operation = EncryptOperation.Update };

        this.validator
            .AnalyzeErrorsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        this.validator
            .AnalyzeWarningsAsync(Arg.Any<BackupRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        this.fileOps.DirectoryExists(Arg.Any<string>()).Returns(true);
        this.fileOps.FileExists(Arg.Any<string>()).Returns(false);

        BackupResult expected = new(true, TimeSpan.FromSeconds(1), 500, 5, 5);
        this.directoryService
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result<BackupResult>.Success(expected));

        Result<BackupResult> result = await orchestrator.ExecuteAsync(request, progress);

        Assert.That(result.Value.IsSuccess, Is.True);
        await directoryService
            .Received(1)
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Is<BackupRequest>(r => r.Operation == EncryptOperation.Update),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>());
    }

    [SetUp]
    public void SetUp()
    {
        this.validator = Substitute.For<IBackupRequestValidator>();
        this.fileOps = Substitute.For<IFileOperationsService>();
        this.singleFileService = Substitute.For<ISingleFileBackupService>();
        this.directoryService = Substitute.For<IDirectoryBackupService>();
        this.progress = Substitute.For<IProgress<BackupStatus>>();

        this.orchestrator = new BackupOrchestrator(
            this.validator,
            this.fileOps,
            this.singleFileService,
            this.directoryService);
    }

    private static BackupRequest CreateRequest() =>
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
