using BackupZCrypt.Application.Backup;
using BackupZCrypt.Application.Common;

namespace BackupZCrypt.Test.Unit.Application.Backup;

/// <summary>
/// Unit tests for <see cref="BackupOutcome"/>, which tells a completed operation apart from one
/// waiting for the user to confirm its warnings.
/// </summary>
public sealed class BackupOutcomeTests
{
    [Fact]
    internal void Completed_CarriesTheResultAndNoPendingWarnings()
    {
        BackupResult result = new(TimeSpan.Zero, 0, 0, 0);

        var outcome = BackupOutcome.Completed(result);

        Assert.Multiple(
            () => Assert.Same(result, outcome.Completion),
            () => Assert.Empty(outcome.PendingWarnings),
            () => Assert.False(outcome.NeedsWarningConfirmation)
        );
    }

    [Fact]
    internal void AwaitingConfirmation_CarriesTheWarningsAndNoResult()
    {
        LocalizableMessage warning = new(MessageCode.DestinationContainsBackup);

        var outcome = BackupOutcome.AwaitingConfirmation([warning]);

        Assert.Multiple(
            () => Assert.Null(outcome.Completion),
            () => Assert.Equal([warning], outcome.PendingWarnings),
            () => Assert.True(outcome.NeedsWarningConfirmation)
        );
    }

    [Fact]
    internal void AwaitingConfirmation_WithoutWarnings_IsRejected()
    {
        _ = Assert.Throws<ArgumentException>(() => BackupOutcome.AwaitingConfirmation([]));
    }
}
