namespace BackupZCrypt.Application.Commands.Interfaces;

/// <summary>
/// Handles a single command type, executing the state-changing operation it describes.
/// </summary>
/// <typeparam name="TCommand">The command type this handler executes.</typeparam>
/// <typeparam name="TResult">The type of result the handler produces.</typeparam>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    /// <summary>
    /// Executes the operation the command describes.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result of the operation.</returns>
    public Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}
