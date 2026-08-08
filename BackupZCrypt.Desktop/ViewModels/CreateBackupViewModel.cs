using BackupZCrypt.Application.Commands;
using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Application.ValueObjects.Password;
using BackupZCrypt.Application.ValueObjects.Settings;
using BackupZCrypt.Desktop.Services;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Domain.Constants;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Backup;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// ViewModel for the create-backup page: collects the source and destination, manages the password
/// (entry, reveal, generation, copy, and strength feedback), and reflects the algorithm defaults from settings.
/// </summary>
/// <param name="createBackup">The handler that executes the create-backup command.</param>
/// <param name="recentPathsQuery">The handler that loads the recently used paths.</param>
/// <param name="saveRecentPathsCommand">The handler that persists the recently used paths.</param>
/// <param name="creationDefaultsQuery">The handler that loads the saved algorithm defaults.</param>
/// <param name="filePicker">The folder picker service.</param>
/// <param name="clipboardService">The clipboard service used to copy a generated password.</param>
/// <param name="generatePassword">The handler that generates a random password.</param>
/// <param name="analyzePasswordStrength">The handler that analyzes password strength.</param>
internal sealed partial class CreateBackupViewModel(
    ICommandHandler<CreateBackupCommand, Result<BackupOutcome>> createBackup,
    IQueryHandler<GetSettingsQuery<RecentPathSettings>, RecentPathSettings> recentPathsQuery,
    ICommandHandler<SaveSettingsCommand<RecentPathSettings>, Result> saveRecentPathsCommand,
    IQueryHandler<GetSettingsQuery<BackupCreationSettings>, BackupCreationSettings> creationDefaultsQuery,
    IFilePickerService filePicker,
    IClipboardService clipboardService,
    ISyncQueryHandler<GeneratePasswordQuery, string> generatePassword,
    ISyncQueryHandler<AnalyzePasswordStrengthQuery, PasswordStrengthAnalysis> analyzePasswordStrength
) : OperationViewModelBase(recentPathsQuery, saveRecentPathsCommand, filePicker)
{
    /// <summary>
    /// The character count of a generated password, set far above the guessable range because a
    /// brute-forced password exposes every chunk of the backup.
    /// </summary>
    private const int GeneratedPasswordLength = 50;

    /// <summary>
    /// The encryption algorithm applied to the new backup, taken from the saved defaults.
    /// </summary>
    private EncryptionAlgorithm encryptionAlgorithm = EncryptionAlgorithm.Aes;

    /// <summary>
    /// The key-derivation algorithm used to turn the password into the master key, taken from the saved defaults.
    /// </summary>
    private KeyDerivationAlgorithm keyDerivationAlgorithm = KeyDerivationAlgorithm.Argon2id;

    /// <summary>
    /// The compression applied to chunks before they are encrypted, taken from the saved defaults.
    /// </summary>
    private CompressionMode compressionMode = CompressionMode.None;

    /// <summary>
    /// Gets or sets the repeated password used to confirm the entry matches.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the password is shown in clear text.
    /// </summary>
    [ObservableProperty]
    public partial bool RevealPassword { get; set; }

    /// <summary>
    /// Gets or sets the estimated strength score of the current password, shown by the strength meter.
    /// </summary>
    [ObservableProperty]
    public partial double StrengthScore { get; set; }

    /// <summary>
    /// Gets or sets the human-readable description of the current password's strength.
    /// </summary>
    [ObservableProperty]
    public partial string StrengthDescription { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the assessed strength of the current password.
    /// </summary>
    /// <remarks>
    /// The View turns this into a colour through a style class. The ViewModel deliberately does not
    /// resolve a brush itself: that would make it reach into Avalonia's theme resources, and the
    /// colour would then be fixed at the moment it was resolved rather than following a theme change.
    /// </remarks>
    [ObservableProperty]
    public partial PasswordStrength Strength { get; set; } = PasswordStrength.VeryWeak;

    /// <summary>
    /// Gets or sets a value indicating whether password-strength feedback is currently shown.
    /// </summary>
    [ObservableProperty]
    public partial bool HasStrength { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the confirmation entry does not match the password,
    /// which is the most common reason the start button is disabled.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowPasswordMismatch { get; set; }

    /// <summary>
    /// Refreshes the encryption state from the latest settings defaults, unless an operation is
    /// currently in flight.
    /// </summary>
    /// <remarks>
    /// The handler absorbs a failure to read the stored defaults into the built-in defaults, so the
    /// page always offers a usable configuration.
    /// </remarks>
    /// <returns>A task that completes once the encryption state has been refreshed.</returns>
    public override async Task OnNavigatedToAsync()
    {
        await base.OnNavigatedToAsync();

        if (IsRunning)
        {
            return;
        }

        var defaults = await creationDefaultsQuery.HandleAsync(
            new GetSettingsQuery<BackupCreationSettings>(),
            CancellationToken.None
        );

        encryptionAlgorithm = Enum.IsDefined(defaults.EncryptionAlgorithm)
            ? defaults.EncryptionAlgorithm
            : EncryptionAlgorithm.Aes;
        keyDerivationAlgorithm = defaults.KeyDerivationAlgorithm;
        compressionMode = defaults.CompressionMode;
    }

    /// <summary>
    /// Seeds the source path from the most recently used source when it is still empty.
    /// </summary>
    /// <param name="recent">The recently used paths.</param>
    protected override void ApplyRecentPaths(RecentPathSettings recent)
    {
        if (string.IsNullOrWhiteSpace(SourcePath) && recent.LastSourcePath is not null)
        {
            SourcePath = recent.LastSourcePath;
        }
    }

    /// <summary>
    /// Builds the create-backup command from the current source, destination, password, and configured
    /// algorithms, and dispatches it to its handler.
    /// </summary>
    /// <param name="proceedOnWarnings">Whether the operation should continue past warnings.</param>
    /// <param name="progress">The sink that receives incremental status updates.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome of the operation.</returns>
    protected override Task<Result<BackupOutcome>> ExecuteOperationAsync(
        bool proceedOnWarnings,
        IProgress<BackupStatus> progress,
        CancellationToken cancellationToken
    )
    {
        var command = new CreateBackupCommand(
            SourcePath,
            DestinationPath,
            Password,
            ConfirmPassword,
            encryptionAlgorithm,
            keyDerivationAlgorithm,
            compressionMode,
            proceedOnWarnings
        )
        {
            Progress = progress,
        };

        return createBackup.HandleAsync(command, cancellationToken);
    }

    /// <summary>
    /// Determines whether the backup can start, additionally requiring a valid, confirmed password so
    /// the user cannot start a backup that would immediately fail password validation.
    /// </summary>
    /// <returns><see langword="true"/> if the backup may begin; otherwise <see langword="false"/>.</returns>
    protected override bool CanStart()
    {
        return base.CanStart() && IsPasswordValid();
    }

    /// <summary>
    /// Determines whether the entered password satisfies the same rules the request validator applies:
    /// within the length bounds, free of leading or trailing whitespace, and matching the confirmation.
    /// </summary>
    /// <returns><see langword="true"/> if the password would pass validation; otherwise <see langword="false"/>.</returns>
    private bool IsPasswordValid()
    {
        return Password.Length >= PasswordConstants.MinLength
            && Password.Length <= PasswordConstants.MaxLength
            && string.Equals(Password.Trim(), Password, StringComparison.Ordinal)
            && string.Equals(Password, ConfirmPassword, StringComparison.Ordinal);
    }

    /// <summary>
    /// Lets the user browse for the folder to back up.
    /// </summary>
    /// <returns>A task that completes once the folder picker has been dismissed.</returns>
    [RelayCommand]
    private Task PickSourceFolderAsync()
    {
        return PickFolderIntoAsync(path => SourcePath = path);
    }

    /// <summary>
    /// Lets the user browse for the folder that will hold the encrypted backup.
    /// </summary>
    /// <returns>A task that completes once the folder picker has been dismissed.</returns>
    [RelayCommand]
    private Task PickDestinationFolderAsync()
    {
        return PickFolderIntoAsync(path => DestinationPath = path);
    }

    /// <summary>
    /// Fills both password fields with a freshly generated password that mixes upper- and lower-case
    /// letters, digits, and symbols, and keeps it masked so it is not left exposed on screen.
    /// </summary>
    [RelayCommand]
    private void GeneratePassword()
    {
        const PasswordGenerationOptions Options =
            PasswordGenerationOptions.IncludeUppercase
            | PasswordGenerationOptions.IncludeLowercase
            | PasswordGenerationOptions.IncludeNumbers
            | PasswordGenerationOptions.IncludeSpecialCharacters;

        var generated = generatePassword.Handle(
            new GeneratePasswordQuery(GeneratedPasswordLength, Options)
        );

        Password = generated;
        ConfirmPassword = generated;
        RevealPassword = false;
    }

    /// <summary>
    /// Gets a value indicating whether there is a password to place on the clipboard.
    /// </summary>
    private bool CanCopyPassword => Password.Length > 0;

    /// <summary>
    /// Copies the current password to the clipboard so the user can store it before starting, since a
    /// lost password makes the backup unrecoverable.
    /// </summary>
    /// <returns>A task that completes once the clipboard has been written.</returns>
    [RelayCommand(CanExecute = nameof(CanCopyPassword))]
    private async Task CopyPasswordAsync()
    {
        await clipboardService.SetTextAsync(Password);
    }

    /// <summary>
    /// Also drops the confirmation entry, which holds a second copy of the same secret.
    /// </summary>
    protected override void ClearPassword()
    {
        base.ClearPassword();
        ConfirmPassword = string.Empty;
    }

    /// <summary>
    /// Re-evaluates the confirmation match and refreshes the strength meter for the new password.
    /// </summary>
    /// <param name="value">The new password.</param>
    protected override void OnPasswordUpdated(string value)
    {
        CopyPasswordCommand.NotifyCanExecuteChanged();
        UpdatePasswordMismatch();

        if (string.IsNullOrEmpty(value))
        {
            HasStrength = false;
            StrengthScore = 0;
            StrengthDescription = string.Empty;
            return;
        }

        var analysis = analyzePasswordStrength.Handle(new AnalyzePasswordStrengthQuery(value));

        HasStrength = true;
        StrengthScore = analysis.Score;
        StrengthDescription = PasswordStrengthFormatter.Format(analysis);
        Strength = analysis.Strength;
    }

    /// <summary>
    /// Re-evaluates whether the confirmation entry still matches the password.
    /// </summary>
    /// <param name="value">The new confirmation entry.</param>
    partial void OnConfirmPasswordChanged(string value)
    {
        UpdatePasswordMismatch();
    }

    /// <summary>
    /// Shows the mismatch hint once the user has typed a confirmation that differs from the password.
    /// </summary>
    private void UpdatePasswordMismatch()
    {
        ShowPasswordMismatch =
            ConfirmPassword.Length > 0
            && !string.Equals(Password, ConfirmPassword, StringComparison.Ordinal);
    }
}
