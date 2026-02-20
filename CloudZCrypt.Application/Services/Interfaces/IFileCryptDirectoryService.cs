namespace CloudZCrypt.Application.Services.Interfaces
{
    using CloudZCrypt.Application.ValueObjects;
    using CloudZCrypt.Domain.ValueObjects.FileCrypt;

    public interface IFileCryptDirectoryService
    {
        Task<Result<FileCryptResult>> ProcessAsync(
            string sourcePath,
            string destinationPath,
            FileCryptRequest request,
            IProgress<FileCryptStatus> progress,
            CancellationToken cancellationToken);
    }
}
