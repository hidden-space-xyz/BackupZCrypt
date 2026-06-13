using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace BackupZCrypt.Desktop.Resources;

/// <summary>
/// Strongly-typed accessor over <c>Strings.resx</c>/<c>Strings.es.resx</c>, consumable from XAML through
/// <c>{x:Static res:Strings.Key}</c>. Each property resolves the resource entry whose name matches the member.
/// </summary>
internal static class Strings
{
    private static readonly ResourceManager ResourceManager = new(
        "BackupZCrypt.Desktop.Resources.Strings",
        typeof(Strings).Assembly
    );

    /// <summary>
    /// Gets the application tagline.
    /// </summary>
    public static string AppTagline => Get();

    /// <summary>
    /// Gets the create-backup navigation label.
    /// </summary>
    public static string NavCreate => Get();

    /// <summary>
    /// Gets the update-backup navigation label.
    /// </summary>
    public static string NavUpdate => Get();

    /// <summary>
    /// Gets the restore-backup navigation label.
    /// </summary>
    public static string NavRestore => Get();

    /// <summary>
    /// Gets the settings navigation label.
    /// </summary>
    public static string NavSettings => Get();

    /// <summary>
    /// Gets the about navigation label.
    /// </summary>
    public static string NavAbout => Get();

    /// <summary>
    /// Gets the create-backup page title.
    /// </summary>
    public static string CreateTitle => Get();

    /// <summary>
    /// Gets the create-backup page subtitle.
    /// </summary>
    public static string CreateSubtitle => Get();

    /// <summary>
    /// Gets the update-backup page title.
    /// </summary>
    public static string UpdateTitle => Get();

    /// <summary>
    /// Gets the update-backup page subtitle.
    /// </summary>
    public static string UpdateSubtitle => Get();

    /// <summary>
    /// Gets the restore-backup page title.
    /// </summary>
    public static string RestoreTitle => Get();

    /// <summary>
    /// Gets the restore-backup page subtitle.
    /// </summary>
    public static string RestoreSubtitle => Get();

    /// <summary>
    /// Gets the settings page title.
    /// </summary>
    public static string SettingsTitle => Get();

    /// <summary>
    /// Gets the settings page subtitle.
    /// </summary>
    public static string SettingsSubtitle => Get();

    /// <summary>
    /// Gets the about page title.
    /// </summary>
    public static string AboutTitle => Get();

    /// <summary>
    /// Gets the about page subtitle.
    /// </summary>
    public static string AboutSubtitle => Get();

    /// <summary>
    /// Gets the locations section heading.
    /// </summary>
    public static string SectionLocations => Get();

    /// <summary>
    /// Gets the security section heading.
    /// </summary>
    public static string SectionSecurity => Get();

    /// <summary>
    /// Gets the options section heading.
    /// </summary>
    public static string SectionOptions => Get();

    /// <summary>
    /// Gets the defaults section heading.
    /// </summary>
    public static string SectionDefaults => Get();

    /// <summary>
    /// Gets the configuration section heading.
    /// </summary>
    public static string SectionConfiguration => Get();

    /// <summary>
    /// Gets the configuration hint text.
    /// </summary>
    public static string ConfigurationHint => Get();

    /// <summary>
    /// Gets the "edit in settings" link text.
    /// </summary>
    public static string EditInSettings => Get();

    /// <summary>
    /// Gets the source field label.
    /// </summary>
    public static string SourceLabel => Get();

    /// <summary>
    /// Gets the source field hint.
    /// </summary>
    public static string SourceHint => Get();

    /// <summary>
    /// Gets the source field hint shown on the update page.
    /// </summary>
    public static string UpdateSourceHint => Get();

    /// <summary>
    /// Gets the destination field label.
    /// </summary>
    public static string DestinationLabel => Get();

    /// <summary>
    /// Gets the destination field hint.
    /// </summary>
    public static string DestinationHint => Get();

    /// <summary>
    /// Gets the backup field label.
    /// </summary>
    public static string BackupLabel => Get();

    /// <summary>
    /// Gets the backup field hint.
    /// </summary>
    public static string BackupHint => Get();

    /// <summary>
    /// Gets the destination field hint shown on the restore page.
    /// </summary>
    public static string RestoreDestinationHint => Get();

    /// <summary>
    /// Gets the browse-folder button text.
    /// </summary>
    public static string BrowseFolder => Get();

    /// <summary>
    /// Gets the browse-file button text.
    /// </summary>
    public static string BrowseFile => Get();

    /// <summary>
    /// Gets the encryption field label.
    /// </summary>
    public static string EncryptionLabel => Get();

    /// <summary>
    /// Gets the key-derivation field label.
    /// </summary>
    public static string KeyDerivationLabel => Get();

    /// <summary>
    /// Gets the compression field label.
    /// </summary>
    public static string CompressionLabel => Get();

    /// <summary>
    /// Gets the display name for the "no encryption" option.
    /// </summary>
    public static string NoneEncryptionName => Get();

    /// <summary>
    /// Gets the description for the "no encryption" option.
    /// </summary>
    public static string NoneEncryptionDescription => Get();

    /// <summary>
    /// Gets the display name for the "no compression" option.
    /// </summary>
    public static string NoneCompressionName => Get();

    /// <summary>
    /// Gets the description for the "no compression" option.
    /// </summary>
    public static string NoneCompressionDescription => Get();

    /// <summary>
    /// Gets the password field label.
    /// </summary>
    public static string PasswordLabel => Get();

    /// <summary>
    /// Gets the password field hint.
    /// </summary>
    public static string PasswordHint => Get();

    /// <summary>
    /// Gets the confirm-password field label.
    /// </summary>
    public static string ConfirmPasswordLabel => Get();

    /// <summary>
    /// Gets the confirm-password field hint.
    /// </summary>
    public static string ConfirmPasswordHint => Get();

    /// <summary>
    /// Gets the reveal-password toggle tooltip.
    /// </summary>
    public static string RevealPasswordTooltip => Get();

    /// <summary>
    /// Gets the generate-password button text.
    /// </summary>
    public static string GenerateButton => Get();

    /// <summary>
    /// Gets the copy button text.
    /// </summary>
    public static string CopyButton => Get();

    /// <summary>
    /// Gets the "copied to clipboard" notice.
    /// </summary>
    public static string CopiedNotice => Get();

    /// <summary>
    /// Gets the "password generated" notice.
    /// </summary>
    public static string GeneratedPasswordNotice => Get();

    /// <summary>
    /// Gets the start-backup button text.
    /// </summary>
    public static string StartBackup => Get();

    /// <summary>
    /// Gets the start-update button text.
    /// </summary>
    public static string StartUpdate => Get();

    /// <summary>
    /// Gets the start-restore button text.
    /// </summary>
    public static string StartRestore => Get();

    /// <summary>
    /// Gets the cancel button text.
    /// </summary>
    public static string CancelButton => Get();

    /// <summary>
    /// Gets the format string for processed/total file progress.
    /// </summary>
    public static string ProgressFilesFormat => Get();

    /// <summary>
    /// Gets the format string for elapsed time.
    /// </summary>
    public static string ElapsedFormat => Get();

    /// <summary>
    /// Gets the message describing a detected encrypted backup.
    /// </summary>
    public static string DetectEncrypted => Get();

    /// <summary>
    /// Gets the message describing a detected unencrypted chunked backup.
    /// </summary>
    public static string DetectUnencrypted => Get();

    /// <summary>
    /// Gets the message describing a detected plain copy.
    /// </summary>
    public static string DetectPlain => Get();

    /// <summary>
    /// Gets the message describing a missing backup.
    /// </summary>
    public static string DetectMissing => Get();

    /// <summary>
    /// Gets the warnings panel title.
    /// </summary>
    public static string WarningsTitle => Get();

    /// <summary>
    /// Gets the "continue anyway" button text.
    /// </summary>
    public static string ContinueAnyway => Get();

    /// <summary>
    /// Gets the dismiss button text.
    /// </summary>
    public static string DismissButton => Get();

    /// <summary>
    /// Gets the title shown when an operation succeeds.
    /// </summary>
    public static string ResultSuccessTitle => Get();

    /// <summary>
    /// Gets the title shown when an operation completes with errors.
    /// </summary>
    public static string ResultPartialTitle => Get();

    /// <summary>
    /// Gets the title shown when an operation fails.
    /// </summary>
    public static string ResultErrorTitle => Get();

    /// <summary>
    /// Gets the title shown when an operation is cancelled.
    /// </summary>
    public static string ResultCancelled => Get();

    /// <summary>
    /// Gets the format string for the processed/total files result.
    /// </summary>
    public static string ResultFilesFormat => Get();

    /// <summary>
    /// Gets the format string for the result duration.
    /// </summary>
    public static string ResultDurationFormat => Get();

    /// <summary>
    /// Gets the format string for the result size.
    /// </summary>
    public static string ResultSizeFormat => Get();

    /// <summary>
    /// Gets the errors panel title.
    /// </summary>
    public static string ErrorsTitle => Get();

    /// <summary>
    /// Gets the language field label.
    /// </summary>
    public static string LanguageLabel => Get();

    /// <summary>
    /// Gets the display text for the system-default language option.
    /// </summary>
    public static string LanguageSystemDefault => Get();

    /// <summary>
    /// Gets the note telling the user a restart is needed to apply a language change.
    /// </summary>
    public static string LanguageRestartNote => Get();

    /// <summary>
    /// Gets the save button text.
    /// </summary>
    public static string SaveButton => Get();

    /// <summary>
    /// Gets the "settings saved" notice.
    /// </summary>
    public static string SettingsSavedNotice => Get();

    /// <summary>
    /// Gets the settings-file path label.
    /// </summary>
    public static string SettingsFileLabel => Get();

    /// <summary>
    /// Gets the format string for the application version caption.
    /// </summary>
    public static string VersionFormat => Get();

    /// <summary>
    /// Gets the folder-picker dialog title.
    /// </summary>
    public static string PickFolderTitle => Get();

    /// <summary>
    /// Gets the file-picker dialog title.
    /// </summary>
    public static string PickFileTitle => Get();

    /// <summary>
    /// Gets the minimize-window tooltip.
    /// </summary>
    public static string WindowMinimize => Get();

    /// <summary>
    /// Gets the maximize-window tooltip.
    /// </summary>
    public static string WindowMaximize => Get();

    /// <summary>
    /// Gets the close-window tooltip.
    /// </summary>
    public static string WindowClose => Get();

    /// <summary>
    /// Gets the AES display name. Resolved by <c>AlgorithmMetadataProvider</c> from the strategy enum value.
    /// </summary>
    public static string AesDisplayName => Get();

    /// <summary>
    /// Gets the AES description.
    /// </summary>
    public static string AesDescription => Get();

    /// <summary>
    /// Gets the AES short summary.
    /// </summary>
    public static string AesSummary => Get();

    /// <summary>
    /// Gets the Serpent display name.
    /// </summary>
    public static string SerpentDisplayName => Get();

    /// <summary>
    /// Gets the Serpent description.
    /// </summary>
    public static string SerpentDescription => Get();

    /// <summary>
    /// Gets the Serpent short summary.
    /// </summary>
    public static string SerpentSummary => Get();

    /// <summary>
    /// Gets the Camellia display name.
    /// </summary>
    public static string CamelliaDisplayName => Get();

    /// <summary>
    /// Gets the Camellia description.
    /// </summary>
    public static string CamelliaDescription => Get();

    /// <summary>
    /// Gets the Camellia short summary.
    /// </summary>
    public static string CamelliaSummary => Get();

    /// <summary>
    /// Gets the ChaCha20 display name.
    /// </summary>
    public static string ChaCha20DisplayName => Get();

    /// <summary>
    /// Gets the ChaCha20 description.
    /// </summary>
    public static string ChaCha20Description => Get();

    /// <summary>
    /// Gets the ChaCha20 short summary.
    /// </summary>
    public static string ChaCha20Summary => Get();

    /// <summary>
    /// Gets the Twofish display name.
    /// </summary>
    public static string TwofishDisplayName => Get();

    /// <summary>
    /// Gets the Twofish description.
    /// </summary>
    public static string TwofishDescription => Get();

    /// <summary>
    /// Gets the Twofish short summary.
    /// </summary>
    public static string TwofishSummary => Get();

    /// <summary>
    /// Gets the PBKDF2 display name.
    /// </summary>
    public static string Pbkdf2DisplayName => Get();

    /// <summary>
    /// Gets the PBKDF2 description.
    /// </summary>
    public static string Pbkdf2Description => Get();

    /// <summary>
    /// Gets the PBKDF2 short summary.
    /// </summary>
    public static string Pbkdf2Summary => Get();

    /// <summary>
    /// Gets the Argon2id display name.
    /// </summary>
    public static string Argon2idDisplayName => Get();

    /// <summary>
    /// Gets the Argon2id description.
    /// </summary>
    public static string Argon2idDescription => Get();

    /// <summary>
    /// Gets the Argon2id short summary.
    /// </summary>
    public static string Argon2idSummary => Get();

    /// <summary>
    /// Gets the scrypt display name.
    /// </summary>
    public static string ScryptDisplayName => Get();

    /// <summary>
    /// Gets the scrypt description.
    /// </summary>
    public static string ScryptDescription => Get();

    /// <summary>
    /// Gets the scrypt short summary.
    /// </summary>
    public static string ScryptSummary => Get();

    /// <summary>
    /// Gets the Zstandard "fast" display name.
    /// </summary>
    public static string ZstdFastDisplayName => Get();

    /// <summary>
    /// Gets the Zstandard "fast" description.
    /// </summary>
    public static string ZstdFastDescription => Get();

    /// <summary>
    /// Gets the Zstandard "fast" short summary.
    /// </summary>
    public static string ZstdFastSummary => Get();

    /// <summary>
    /// Gets the Zstandard "default" display name.
    /// </summary>
    public static string ZstdDisplayName => Get();

    /// <summary>
    /// Gets the Zstandard "default" description.
    /// </summary>
    public static string ZstdDescription => Get();

    /// <summary>
    /// Gets the Zstandard "default" short summary.
    /// </summary>
    public static string ZstdSummary => Get();

    /// <summary>
    /// Gets the Zstandard "best" display name.
    /// </summary>
    public static string ZstdBestDisplayName => Get();

    /// <summary>
    /// Gets the Zstandard "best" description.
    /// </summary>
    public static string ZstdBestDescription => Get();

    /// <summary>
    /// Gets the Zstandard "best" short summary.
    /// </summary>
    public static string ZstdBestSummary => Get();

    private static string Get([CallerMemberName] string name = "")
    {
        return ResourceManager.GetString(name, CultureInfo.CurrentUICulture) ?? name;
    }

    /// <summary>
    /// Dynamically resolves a resource string by its key, used for <c>MessageCode</c>-keyed strings
    /// looked up by enum member name (by <c>MessageLocalizer</c> and <c>PasswordStrengthFormatter</c>).
    /// </summary>
    /// <param name="key">The resource key, typically a <c>MessageCode</c> member name.</param>
    /// <returns>The localized string, or the key itself when no matching resource exists.</returns>
    internal static string GetByKey(string key)
    {
        return ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }
}
