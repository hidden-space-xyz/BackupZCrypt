using CloudZCrypt.Application.Orchestrators;
using CloudZCrypt.Application.Services.Interfaces;
using CloudZCrypt.Application.Validators.Interfaces;
using CloudZCrypt.Application.ValueObjects;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Services.Interfaces;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;
using NSubstitute;

namespace CloudZCrypt.Test.Application.Orchestrators;

[TestFixture]
internal sealed class FileCryptOrchestratorTests
{
    private IFileCryptRequestValidator _validator = null!;
    private IFileOperationsService _fileOps = null!;
    private IFileCryptSingleFileService _singleFileService = null!;
    private IFileCryptDirectoryService _directoryService = null!;
    private FileCryptOrchestrator _orchestrator = null!;
    private IProgress<FileCryptStatus> _progress = null!;

    [SetUp]
    public void SetUp()
    {
        _validator = Substitute.For<IFileCryptRequestValidator>();
        _fileOps = Substitute.For<IFileOperationsService>();
        _singleFileService = Substitute.For<IFileCryptSingleFileService>();
        _directoryService = Substitute.For<IFileCryptDirectoryService>();
        _progress = Substitute.For<IProgress<FileCryptStatus>>();

        _orchestrator = new FileCryptOrchestrator(
            _validator,
            _fileOps,
            _singleFileService,
            _directoryService
        );
    }

    private static FileCryptRequest CreateRequest() =>
        new(
            @"C:\source\file.txt",
            @"C:\dest\file.czc",
            "StrongP@ss1",
            "StrongP@ss1",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            EncryptOperation.Encrypt,
            NameObfuscationMode.None
        );

    [Test]
    public async Task Execute_WithValidationErrors_ReturnsResultWithErrors()
    {
        _validator
            .AnalyzeErrorsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string> { "error1" });

        Result<FileCryptResult> result = await _orchestrator.ExecuteAsync(
            CreateRequest(),
            _progress
        );

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Errors, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Execute_WithWarningsAndNotProceeding_ReturnsWarnings()
    {
        _validator
            .AnalyzeErrorsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        _validator
            .AnalyzeWarningsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string> { "warning1" });

        Result<FileCryptResult> result = await _orchestrator.ExecuteAsync(
            CreateRequest(),
            _progress
        );

        Assert.That(result.Value.HasWarnings, Is.True);
    }

    [Test]
    public async Task Execute_WithWarningsAndProceedOnWarnings_ProcessesFile()
    {
        FileCryptRequest request = CreateRequest() with { ProceedOnWarnings = true };

        _validator
            .AnalyzeErrorsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        _validator
            .AnalyzeWarningsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string> { "warning1" });

        _fileOps.FileExists(Arg.Any<string>()).Returns(true);
        _fileOps.DirectoryExists(Arg.Any<string>()).Returns(false);
        _fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");

        FileCryptResult expected = new(true, TimeSpan.FromSeconds(1), 100, 1, 1);
        _singleFileService
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<FileCryptRequest>(),
                Arg.Any<IProgress<FileCryptStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result<FileCryptResult>.Success(expected));

        Result<FileCryptResult> result = await _orchestrator.ExecuteAsync(request, _progress);

        Assert.That(result.Value.IsSuccess, Is.True);
    }

    [Test]
    public async Task Execute_SourceDoesNotExist_ReturnsFailure()
    {
        _validator
            .AnalyzeErrorsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        _validator
            .AnalyzeWarningsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        _fileOps.FileExists(Arg.Any<string>()).Returns(false);
        _fileOps.DirectoryExists(Arg.Any<string>()).Returns(false);

        Result<FileCryptResult> result = await _orchestrator.ExecuteAsync(
            CreateRequest(),
            _progress
        );

        Assert.That(result.IsSuccess, Is.False);
    }

    [Test]
    public async Task Execute_FileSource_DelegatesToSingleFileService()
    {
        _validator
            .AnalyzeErrorsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        _validator
            .AnalyzeWarningsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        _fileOps.DirectoryExists(Arg.Any<string>()).Returns(false);
        _fileOps.FileExists(Arg.Any<string>()).Returns(true);
        _fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");

        FileCryptResult expected = new(true, TimeSpan.FromSeconds(1), 100, 1, 1);
        _singleFileService
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<FileCryptRequest>(),
                Arg.Any<IProgress<FileCryptStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result<FileCryptResult>.Success(expected));

        Result<FileCryptResult> result = await _orchestrator.ExecuteAsync(
            CreateRequest(),
            _progress
        );

        Assert.That(result.Value.IsSuccess, Is.True);
        await _singleFileService
            .Received(1)
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<FileCryptRequest>(),
                Arg.Any<IProgress<FileCryptStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task Execute_DirectorySource_DelegatesToDirectoryService()
    {
        _validator
            .AnalyzeErrorsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        _validator
            .AnalyzeWarningsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        _fileOps.DirectoryExists(Arg.Any<string>()).Returns(true);
        _fileOps.FileExists(Arg.Any<string>()).Returns(false);

        FileCryptResult expected = new(true, TimeSpan.FromSeconds(1), 500, 5, 5);
        _directoryService
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<FileCryptRequest>(),
                Arg.Any<IProgress<FileCryptStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result<FileCryptResult>.Success(expected));

        Result<FileCryptResult> result = await _orchestrator.ExecuteAsync(
            CreateRequest(),
            _progress
        );

        Assert.That(result.Value.IsSuccess, Is.True);
        await _directoryService
            .Received(1)
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<FileCryptRequest>(),
                Arg.Any<IProgress<FileCryptStatus>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task Execute_UnexpectedException_ReturnsFailure()
    {
        _validator
            .AnalyzeErrorsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        _validator
            .AnalyzeWarningsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        _fileOps.FileExists(Arg.Any<string>()).Returns(true);
        _fileOps.DirectoryExists(Arg.Any<string>()).Returns(false);
        _fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");

        _singleFileService
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<FileCryptRequest>(),
                Arg.Any<IProgress<FileCryptStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Result<FileCryptResult>>(_ => throw new InvalidOperationException("boom"));

        Result<FileCryptResult> result = await _orchestrator.ExecuteAsync(
            CreateRequest(),
            _progress
        );

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Errors, Has.Some.Contains("unexpected"));
    }

    [Test]
    public void Execute_Cancellation_ThrowsOperationCanceled()
    {
        _validator
            .AnalyzeErrorsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());
        _validator
            .AnalyzeWarningsAsync(Arg.Any<FileCryptRequest>(), Arg.Any<CancellationToken>())
            .Returns(new List<string>());

        _fileOps.FileExists(Arg.Any<string>()).Returns(true);
        _fileOps.DirectoryExists(Arg.Any<string>()).Returns(false);
        _fileOps.GetDirectoryName(Arg.Any<string>()).Returns(@"C:\dest");

        _singleFileService
            .ProcessAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<FileCryptRequest>(),
                Arg.Any<IProgress<FileCryptStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Result<FileCryptResult>>(_ => throw new OperationCanceledException());

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _orchestrator.ExecuteAsync(CreateRequest(), _progress)
        );
    }
}
