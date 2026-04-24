namespace BackupZCrypt.Terminal.Services.Interfaces;

internal interface IPathPromptService
{
    Task<string> PromptSourcePathAsync();

    Task<string> PromptDestinationPathAsync();

    Task<string> PromptUpdateSourcePathAsync();

    Task<string> PromptUpdateBackupPathAsync();

    Task RememberPathsAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);
}
