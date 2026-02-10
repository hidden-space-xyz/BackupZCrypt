using CloudZCrypt.Application.ValueObjects;
using CloudZCrypt.Domain.ValueObjects.FileCrypt;

namespace CloudZCrypt.Application.Services.Interfaces
{
    public interface IFileCryptSingleFileService
    {
        Task<Result<FileCryptResult>> ProcessAsync(
            string sourcePath,
            string destinationPath,
            FileCryptRequest request,
            IProgress<FileCryptStatus> progress,
            CancellationToken cancellationToken
        );
    }
}
