namespace BackupZCrypt.Test.Application.Services;

using BackupZCrypt.Application.Services;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;
using NSubstitute;

[TestFixture]
internal sealed class DirectoryBackupServiceTests
{
    private IChunkedBackupService chunkedBackupService = null!;
    private IProgress<BackupStatus> progress = null!;
    private DirectoryBackupService service = null!;

    [SetUp]
    public void SetUp()
    {
        this.chunkedBackupService = Substitute.For<IChunkedBackupService>();
        this.progress = Substitute.For<IProgress<BackupStatus>>();
        this.service = new DirectoryBackupService(this.chunkedBackupService);
    }

    [Test]
    public async Task ProcessAsync_Create_DelegatesToChunkedCreate()
    {
        var expected = Result<BackupResult>.Success(
            new BackupResult(true, TimeSpan.FromSeconds(1), 100, 1, 1));

        this.chunkedBackupService.CreateAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(), Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await this.service.ProcessAsync(
            @"C:\source", @"C:\dest",
            CreateRequest(BackupOperation.Create),
            this.progress, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        await this.chunkedBackupService.Received(1).CreateAsync(
            @"C:\source", @"C:\dest", Arg.Any<BackupRequest>(),
            this.progress, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_Update_DelegatesToChunkedUpdate()
    {
        var expected = Result<BackupResult>.Success(
            new BackupResult(true, TimeSpan.FromSeconds(1), 100, 1, 1));

        this.chunkedBackupService.UpdateAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(), Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await this.service.ProcessAsync(
            @"C:\source", @"C:\dest",
            CreateRequest(BackupOperation.Update),
            this.progress, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        await this.chunkedBackupService.Received(1).UpdateAsync(
            @"C:\source", @"C:\dest", Arg.Any<BackupRequest>(),
            this.progress, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessAsync_Restore_DelegatesToChunkedRestore()
    {
        var expected = Result<BackupResult>.Success(
            new BackupResult(true, TimeSpan.FromSeconds(1), 100, 1, 1));

        this.chunkedBackupService.RestoreAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<BackupRequest>(),
                Arg.Any<IProgress<BackupStatus>>(), Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await this.service.ProcessAsync(
            @"C:\source", @"C:\dest",
            CreateRequest(BackupOperation.Restore),
            this.progress, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        await this.chunkedBackupService.Received(1).RestoreAsync(
            @"C:\source", @"C:\dest", Arg.Any<BackupRequest>(),
            this.progress, Arg.Any<CancellationToken>());
    }

    private static BackupRequest CreateRequest(
        BackupOperation operation = BackupOperation.Create) =>
        new(
            @"C:\source",
            @"C:\dest",
            "StrongP@ss1",
            "StrongP@ss1",
            EncryptionAlgorithm.Aes,
            KeyDerivationAlgorithm.Argon2id,
            operation);
}