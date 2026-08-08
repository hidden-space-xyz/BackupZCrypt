using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.ValueObjects.Backup;

/// <summary>
/// The outcome of a backup operation that was allowed to proceed: either the completed engine
/// result, or the advisory warnings awaiting the user's confirmation before the operation runs.
/// </summary>
public sealed record class BackupOutcome
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BackupOutcome"/> class.
    /// </summary>
    /// <param name="completion">The engine result, or <see langword="null"/> while warnings await confirmation.</param>
    /// <param name="pendingWarnings">The warnings awaiting confirmation; empty when completed.</param>
    private BackupOutcome(BackupResult? completion, IReadOnlyList<LocalizableMessage> pendingWarnings)
    {
        this.Completion = completion;
        this.PendingWarnings = pendingWarnings;
    }

    /// <summary>
    /// Gets the engine result, or <see langword="null"/> while warnings await confirmation.
    /// </summary>
    public BackupResult? Completion { get; }

    /// <summary>
    /// Gets the warnings the user must confirm before the operation runs; empty once completed.
    /// </summary>
    public IReadOnlyList<LocalizableMessage> PendingWarnings { get; }

    /// <summary>
    /// Gets a value indicating whether the operation stopped to ask about warnings instead of running.
    /// </summary>
    public bool NeedsWarningConfirmation => this.Completion is null;

    /// <summary>
    /// Wraps the result of an operation that ran to completion, whether fully or partially successful.
    /// </summary>
    /// <param name="result">The result the engine reported.</param>
    /// <returns>An outcome carrying the completed result and no pending warnings.</returns>
    public static BackupOutcome Completed(BackupResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new(result, []);
    }

    /// <summary>
    /// Wraps the advisory warnings that stopped the operation before it ran, awaiting the user's
    /// decision to proceed.
    /// </summary>
    /// <param name="warnings">The warnings the user must confirm; must contain at least one.</param>
    /// <returns>An outcome carrying the pending warnings and no completed result.</returns>
    /// <exception cref="ArgumentException"><paramref name="warnings"/> is empty.</exception>
    public static BackupOutcome AwaitingConfirmation(IReadOnlyList<LocalizableMessage> warnings)
    {
        ArgumentNullException.ThrowIfNull(warnings);

        return warnings.Count > 0
            ? new(null, warnings)
            : throw new ArgumentException(
                "An outcome awaiting confirmation must carry at least one warning.",
                nameof(warnings)
            );
    }
}
