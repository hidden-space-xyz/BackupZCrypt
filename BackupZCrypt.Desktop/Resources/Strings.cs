using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace BackupZCrypt.Desktop.Resources;

// Strongly-typed accessor over Strings.resx/.es.resx, consumable from XAML
// through {x:Static res:Strings.Key}.
internal static class Strings
{
    private static readonly ResourceManager ResourceManager = new(
        "BackupZCrypt.Desktop.Resources.Strings",
        typeof(Strings).Assembly
    );

    public static string AppTagline => Get();

    public static string NavCreate => Get();

    public static string NavUpdate => Get();

    public static string NavRestore => Get();

    public static string NavSettings => Get();

    public static string NavAbout => Get();

    public static string CreateTitle => Get();

    public static string CreateSubtitle => Get();

    public static string UpdateTitle => Get();

    public static string UpdateSubtitle => Get();

    public static string RestoreTitle => Get();

    public static string RestoreSubtitle => Get();

    public static string SettingsTitle => Get();

    public static string SettingsSubtitle => Get();

    public static string AboutTitle => Get();

    public static string AboutSubtitle => Get();

    public static string SectionLocations => Get();

    public static string SectionSecurity => Get();

    public static string SectionOptions => Get();

    public static string SectionDefaults => Get();

    public static string SectionConfiguration => Get();

    public static string ConfigurationHint => Get();

    public static string EditInSettings => Get();

    public static string SourceLabel => Get();

    public static string SourceHint => Get();

    public static string UpdateSourceHint => Get();

    public static string DestinationLabel => Get();

    public static string DestinationHint => Get();

    public static string BackupLabel => Get();

    public static string BackupHint => Get();

    public static string RestoreDestinationHint => Get();

    public static string BrowseFolder => Get();

    public static string BrowseFile => Get();

    public static string EncryptionLabel => Get();

    public static string KeyDerivationLabel => Get();

    public static string CompressionLabel => Get();

    public static string NoneEncryptionName => Get();

    public static string NoneEncryptionDescription => Get();

    public static string NoneCompressionName => Get();

    public static string NoneCompressionDescription => Get();

    public static string PasswordLabel => Get();

    public static string PasswordHint => Get();

    public static string ConfirmPasswordLabel => Get();

    public static string ConfirmPasswordHint => Get();

    public static string RevealPasswordTooltip => Get();

    public static string GenerateButton => Get();

    public static string CopyButton => Get();

    public static string CopiedNotice => Get();

    public static string GeneratedPasswordNotice => Get();

    public static string StartBackup => Get();

    public static string StartUpdate => Get();

    public static string StartRestore => Get();

    public static string CancelButton => Get();

    public static string ProgressFilesFormat => Get();

    public static string ElapsedFormat => Get();

    public static string DetectEncrypted => Get();

    public static string DetectUnencrypted => Get();

    public static string DetectPlain => Get();

    public static string DetectMissing => Get();

    public static string WarningsTitle => Get();

    public static string ContinueAnyway => Get();

    public static string DismissButton => Get();

    public static string ResultSuccessTitle => Get();

    public static string ResultPartialTitle => Get();

    public static string ResultErrorTitle => Get();

    public static string ResultCancelled => Get();

    public static string ResultFilesFormat => Get();

    public static string ResultDurationFormat => Get();

    public static string ResultSizeFormat => Get();

    public static string ErrorsTitle => Get();

    public static string LanguageLabel => Get();

    public static string LanguageSystemDefault => Get();

    public static string LanguageRestartNote => Get();

    public static string SaveButton => Get();

    public static string SettingsSavedNotice => Get();

    public static string SettingsFileLabel => Get();

    public static string VersionFormat => Get();

    public static string PickFolderTitle => Get();

    public static string PickFileTitle => Get();

    public static string WindowMinimize => Get();

    public static string WindowMaximize => Get();

    public static string WindowClose => Get();

    // Algorithm display text (moved from Infrastructure). Resolved by name through
    // AlgorithmMetadataProvider, which maps each strategy enum value to these keys.
    public static string AesDisplayName => Get();

    public static string AesDescription => Get();

    public static string AesSummary => Get();

    public static string SerpentDisplayName => Get();

    public static string SerpentDescription => Get();

    public static string SerpentSummary => Get();

    public static string CamelliaDisplayName => Get();

    public static string CamelliaDescription => Get();

    public static string CamelliaSummary => Get();

    public static string ChaCha20DisplayName => Get();

    public static string ChaCha20Description => Get();

    public static string ChaCha20Summary => Get();

    public static string TwofishDisplayName => Get();

    public static string TwofishDescription => Get();

    public static string TwofishSummary => Get();

    public static string Pbkdf2DisplayName => Get();

    public static string Pbkdf2Description => Get();

    public static string Pbkdf2Summary => Get();

    public static string Argon2idDisplayName => Get();

    public static string Argon2idDescription => Get();

    public static string Argon2idSummary => Get();

    public static string ScryptDisplayName => Get();

    public static string ScryptDescription => Get();

    public static string ScryptSummary => Get();

    public static string ZstdFastDisplayName => Get();

    public static string ZstdFastDescription => Get();

    public static string ZstdFastSummary => Get();

    public static string ZstdDisplayName => Get();

    public static string ZstdDescription => Get();

    public static string ZstdSummary => Get();

    public static string ZstdBestDisplayName => Get();

    public static string ZstdBestDescription => Get();

    public static string ZstdBestSummary => Get();

    private static string Get([CallerMemberName] string name = "")
    {
        return ResourceManager.GetString(name, CultureInfo.CurrentUICulture) ?? name;
    }

    // Dynamic lookup for MessageCode-keyed strings (Bucket B), resolved by the
    // enum member name. Used by MessageLocalizer / PasswordStrengthFormatter.
    internal static string GetByKey(string key)
    {
        return ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    }
}
