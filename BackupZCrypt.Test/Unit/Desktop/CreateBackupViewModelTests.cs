using BackupZCrypt.Application.Algorithms;
using BackupZCrypt.Application.Backup;
using BackupZCrypt.Application.Common;
using BackupZCrypt.Application.Passwords;
using BackupZCrypt.Application.Settings;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Desktop.ViewModels;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Test.Common;

using Microsoft.Extensions.DependencyInjection;

using NSubstitute;

namespace BackupZCrypt.Test.Unit.Desktop;

/// <summary>
/// Unit tests for the create-backup page: the password gate it duplicates from the request validator,
/// password generation and copying, the strength meter wiring, and the algorithm defaults it reads
/// from settings.
/// </summary>
public sealed class CreateBackupViewModelTests
{
    /// <summary>
    /// The error codes the request validator raises about the password entry. Only these decide
    /// whether the page's own start gate agrees with the validator.
    /// </summary>
    private static readonly MessageCode[] PasswordErrorCodes =
    [
        MessageCode.PasswordRequired,
        MessageCode.PasswordTooShort,
        MessageCode.PasswordTooLong,
        MessageCode.PasswordLeadingTrailingSpaces,
        MessageCode.ConfirmPasswordRequired,
        MessageCode.PasswordMismatch,
    ];

    /// <summary>
    /// A password of exactly the longest accepted length, so the pair of rules can be probed on both
    /// sides of the upper bound without either side restating the constant.
    /// </summary>
    private static readonly string LongestAcceptedPassword = new('a', 1000);

    /// <summary>
    /// The password and confirmation pairs that probe every boundary of the duplicated rules:
    /// emptiness, both length bounds, surrounding whitespace, and confirmation mismatches.
    /// </summary>
    private static readonly (string Password, string Confirm)[] PasswordCandidates =
    [
        ("", ""),
        ("1234567", "1234567"),
        ("12345678", "12345678"),
        (LongestAcceptedPassword, LongestAcceptedPassword),
        (LongestAcceptedPassword + "a", LongestAcceptedPassword + "a"),
        ("        ", "        "),
        (" leading8", " leading8"),
        ("trailing8 ", "trailing8 "),
        ("good-password", "good-password"),
        ("good-password", "other-password"),
        ("good-password", ""),
    ];

    /// <summary>
    /// The substituted backup operations the page runs the create request through.
    /// </summary>
    private readonly IBackupOperations operations = Substitute.For<IBackupOperations>();

    /// <summary>
    /// The substituted store supplying the remembered paths and the saved algorithm defaults.
    /// </summary>
    private readonly ISettingsStore settingsStore = Substitute.For<ISettingsStore>();

    /// <summary>
    /// The substituted folder picker, never exercised here but required by the constructor.
    /// </summary>
    private readonly IFilePickerService filePicker = Substitute.For<IFilePickerService>();

    /// <summary>
    /// The substituted clipboard the generated password is copied to.
    /// </summary>
    private readonly IClipboardService clipboardService = Substitute.For<IClipboardService>();

    /// <summary>
    /// The substituted service behind password generation and the strength meter.
    /// </summary>
    private readonly IPasswordService passwordService = Substitute.For<IPasswordService>();

    /// <summary>
    /// The catalog of algorithms the page may preselect, offering every algorithm unless a test narrows it.
    /// </summary>
    private IAlgorithmCatalog algorithmCatalog = AlgorithmCatalogs.Every();

    [Fact]
    internal async Task StartCommand_CanExecute_AgreesWithTheRequestValidatorOnEveryPasswordRule()
    {
        using TempDir temp = new();
        _ = temp.WriteText(Path.Join("source", "file.txt"), "content");
        var source = temp.Combine("source");
        var destination = temp.Combine("destination");
        _ = Directory.CreateDirectory(destination);

        await using var provider = TestHost.CreateProvider();
        var validator = provider.GetRequiredService<IBackupRequestValidator>();

        var sut = CreateSut();
        sut.SourcePath = source;
        sut.DestinationPath = destination;

        List<string> drift = [];

        foreach (var (password, confirm) in PasswordCandidates)
        {
            sut.Password = password;
            sut.ConfirmPassword = confirm;

            var errors = await validator.AnalyzeErrorsAsync(
                new CreateBackupRequest(
                    source,
                    destination,
                    password,
                    confirm,
                    EncryptionAlgorithm.Aes,
                    KeyDerivationAlgorithm.PBKDF2
                ),
                TestContext.Current.CancellationToken
            );

            var validatorAccepts = !errors.Any(error =>
                PasswordErrorCodes.Contains(error.Code)
            );
            var pageAccepts = sut.StartCommand.CanExecute(null);

            if (pageAccepts != validatorAccepts)
            {
                drift.Add(
                    $"'{Describe(password)}'/'{Describe(confirm)}': "
                        + $"page={pageAccepts}, validator={validatorAccepts}"
                );
            }
        }

        Assert.Empty(drift);
    }

    [Fact]
    internal void GeneratePasswordCommand_FillsBothFieldsWithALongPasswordUsingEveryCharacterClass()
    {
        var sut = CreateSut();
        const PasswordGenerationOptions EveryCharacterClass =
            PasswordGenerationOptions.IncludeUppercase
            | PasswordGenerationOptions.IncludeLowercase
            | PasswordGenerationOptions.IncludeNumbers
            | PasswordGenerationOptions.IncludeSpecialCharacters;

        _ = this
            .passwordService.GeneratePassword(Arg.Any<int>(), Arg.Any<PasswordGenerationOptions>())
            .Returns("GENERATED-PASSWORD");

        sut.RevealPassword = true;
        sut.ConfirmPassword = "stale-confirmation";

        sut.GeneratePasswordCommand.Execute(null);

        Assert.Multiple(
            () => _ = this.passwordService.Received(1).GeneratePassword(50, EveryCharacterClass),
            () => Assert.Equal("GENERATED-PASSWORD", sut.Password),
            () => Assert.Equal("GENERATED-PASSWORD", sut.ConfirmPassword),
            () => Assert.False(sut.RevealPassword),
            () => Assert.False(sut.ShowPasswordMismatch)
        );
    }

    [Fact]
    internal async Task CopyPasswordCommand_IsGatedOnAPasswordAndCopiesItVerbatimAsASecret()
    {
        var sut = CreateSut();
        List<string> copied = [];
        _ = this.clipboardService.SetSensitiveTextAsync(Arg.Do<string>(copied.Add))
            .Returns(Task.CompletedTask);

        var enabledWithoutPassword = sut.CopyPasswordCommand.CanExecute(null);

        sut.Password = "  pass word  ";
        var enabledWithPassword = sut.CopyPasswordCommand.CanExecute(null);

        await sut.CopyPasswordCommand.ExecuteAsync(null);

        sut.Password = string.Empty;

        string[] expectedCopied = ["  pass word  "];

        Assert.Multiple(
            () => Assert.False(enabledWithoutPassword),
            () => Assert.True(enabledWithPassword),
            () => Assert.Equal(expectedCopied, copied),
            () => Assert.False(sut.CopyPasswordCommand.CanExecute(null))
        );
    }

    [Fact]
    internal void Password_WhenSetAndThenCleared_PublishesAndThenClearsTheStrengthFeedback()
    {
        var sut = CreateSut();
        PasswordStrengthAnalysis analysis = new(
            PasswordStrength.Weak,
            22,
            31.4,
            [MessageCode.TipIncreaseLength]
        );
        _ = this
            .passwordService.AnalyzePasswordStrength("weak-password")
            .Returns(analysis);

        sut.Password = "weak-password";
        var hasStrength = sut.HasStrength;
        var score = sut.StrengthScore;
        var description = sut.StrengthDescription;

        sut.Password = string.Empty;

        Assert.Multiple(
            () => Assert.True(hasStrength),
            () => Assert.Equal(22d, score),
            () => Assert.Equal(PasswordStrengthFormatter.Format(analysis), description),
            () => Assert.False(sut.HasStrength),
            () => Assert.Equal(0d, sut.StrengthScore),
            () => Assert.Empty(sut.StrengthDescription)
        );
    }

    [Theory]
    [InlineData("abcdefgh", "", false)]
    [InlineData("abcdefgh", "abc", true)]
    [InlineData("abcdefgh", "abcdefgh", false)]
    internal void ConfirmPassword_WhenTyped_ShowsTheMismatchHintOnlyOnceSomethingWasEntered(
        string password,
        string confirm,
        bool expected
    )
    {
        var sut = CreateSut();

        sut.Password = password;
        sut.ConfirmPassword = confirm;

        Assert.Equal(expected, sut.ShowPasswordMismatch);
    }

    [Fact]
    internal async Task OnNavigatedToAsync_WithAnUndefinedStoredEncryptionAlgorithm_FallsBackToAes()
    {
        var sut = CreateSut();
        _ = this
            .settingsStore.LoadAsync<BackupCreationSettings>(Arg.Any<CancellationToken>())
            .Returns(
                new BackupCreationSettings(
                    (EncryptionAlgorithm)99,
                    KeyDerivationAlgorithm.Scrypt,
                    CompressionMode.ZstdBest
                )
            );

        var requests = StubCreateCapturingRequests();

        await sut.OnNavigatedToAsync();

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";
        sut.Password = "good-password";
        sut.ConfirmPassword = "good-password";

        await sut.StartCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.Single(requests),
            () => Assert.Equal(EncryptionAlgorithm.Aes, requests[0].EncryptionAlgorithm),
            () => Assert.Equal(KeyDerivationAlgorithm.Scrypt, requests[0].KeyDerivationAlgorithm),
            () => Assert.Equal(CompressionMode.ZstdBest, requests[0].Compression),
            () => Assert.Equal("good-password", requests[0].Password),
            () => Assert.Equal("good-password", requests[0].ConfirmPassword),
            () => Assert.False(requests[0].ProceedOnWarnings),
            () =>
                Assert.Equal(
                    AlgorithmMetadataProvider.GetName(EncryptionAlgorithm.Aes),
                    sut.EncryptionName
                )
        );
    }

    [Theory]
    [InlineData("short", "PasswordHintTooShort")]
    [InlineData(" Spaced-Passw0rd!", "PasswordHintSpaces")]
    [InlineData("Spaced-Passw0rd! ", "PasswordHintSpaces")]
    [InlineData("Valid-Passw0rd!", "")]
    internal void Password_BreakingALengthOrSpacingRule_ExplainsWhyStartIsDisabled(
        string password,
        string expectedHintKey
    )
    {
        var sut = CreateSut();

        sut.Password = password;

        var expected = expectedHintKey.Length is 0 ? string.Empty : Strings.GetByKey(expectedHintKey);

        Assert.Multiple(
            () => Assert.Equal(expected, sut.PasswordRuleHint),
            () => Assert.Equal(expected.Length > 0, sut.ShowPasswordRuleHint)
        );
    }

    [Fact]
    internal void Password_LongerThanTheMaximum_ExplainsTheLimit()
    {
        var sut = CreateSut();

        sut.Password = new string('x', 1001);

        Assert.Equal(Strings.PasswordHintTooLong, sut.PasswordRuleHint);
    }

    [Fact]
    internal async Task OnNavigatedToAsync_StoredAlgorithmsThatDoNotExist_FallBackToTheDefaultsShown()
    {
        var sut = CreateSut();
        _ = this
            .settingsStore.LoadAsync<BackupCreationSettings>(Arg.Any<CancellationToken>())
            .Returns(
                new BackupCreationSettings(
                    (EncryptionAlgorithm)77,
                    (KeyDerivationAlgorithm)77,
                    (CompressionMode)77
                )
            );

        await sut.OnNavigatedToAsync();

        Assert.Multiple(
            () => Assert.Equal(AlgorithmMetadataProvider.GetName(EncryptionAlgorithm.Aes), sut.EncryptionName),
            () => Assert.Equal(AlgorithmMetadataProvider.GetName(KeyDerivationAlgorithm.Argon2id), sut.KeyDerivationName),
            () => Assert.Equal(AlgorithmMetadataProvider.GetName(CompressionMode.None), sut.CompressionName)
        );
    }

    [Fact]
    internal async Task OnNavigatedToAsync_StoredAlgorithmsThisBuildDoesNotOffer_FallBackToTheDefaults()
    {
        this.algorithmCatalog = AlgorithmCatalogs.Offering(
            [EncryptionAlgorithm.Aes, EncryptionAlgorithm.ChaCha20],
            [KeyDerivationAlgorithm.Argon2id],
            [CompressionMode.None, CompressionMode.Zstd]
        );
        var sut = CreateSut();
        _ = this
            .settingsStore.LoadAsync<BackupCreationSettings>(Arg.Any<CancellationToken>())
            .Returns(
                new BackupCreationSettings(
                    EncryptionAlgorithm.Serpent,
                    KeyDerivationAlgorithm.Scrypt,
                    CompressionMode.Zstd
                )
            );

        await sut.OnNavigatedToAsync();

        Assert.Multiple(
            () => Assert.Equal(AlgorithmMetadataProvider.GetName(EncryptionAlgorithm.Aes), sut.EncryptionName),
            () => Assert.Equal(AlgorithmMetadataProvider.GetName(KeyDerivationAlgorithm.Argon2id), sut.KeyDerivationName),
            () => Assert.Equal(AlgorithmMetadataProvider.GetName(CompressionMode.Zstd), sut.CompressionName)
        );
    }

    [Fact]
    internal async Task OnNavigatedToAsync_ShowsTheNamesOfTheAlgorithmsTheNextBackupWillUse()
    {
        var sut = CreateSut();
        _ = this
            .settingsStore.LoadAsync<BackupCreationSettings>(Arg.Any<CancellationToken>())
            .Returns(
                new BackupCreationSettings(
                    EncryptionAlgorithm.Serpent,
                    KeyDerivationAlgorithm.Scrypt,
                    CompressionMode.ZstdBest
                )
            );

        string[] namesBeforeLoading = [sut.EncryptionName, sut.KeyDerivationName, sut.CompressionName];
        List<string?> changed = [];
        sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await sut.OnNavigatedToAsync();

        Assert.Multiple(
            () =>
                Assert.Equal<string>(
                    [
                        AlgorithmMetadataProvider.GetName(EncryptionAlgorithm.Aes),
                        AlgorithmMetadataProvider.GetName(KeyDerivationAlgorithm.Argon2id),
                        AlgorithmMetadataProvider.GetName(CompressionMode.None),
                    ],
                    namesBeforeLoading
                ),
            () =>
                Assert.Equal(
                    AlgorithmMetadataProvider.GetName(EncryptionAlgorithm.Serpent),
                    sut.EncryptionName
                ),
            () =>
                Assert.Equal(
                    AlgorithmMetadataProvider.GetName(KeyDerivationAlgorithm.Scrypt),
                    sut.KeyDerivationName
                ),
            () =>
                Assert.Equal(
                    AlgorithmMetadataProvider.GetName(CompressionMode.ZstdBest),
                    sut.CompressionName
                ),
            () =>
                Assert.Superset(
                    new HashSet<string?>(StringComparer.Ordinal)
                    {
                        nameof(CreateBackupViewModel.EncryptionName),
                        nameof(CreateBackupViewModel.KeyDerivationName),
                        nameof(CreateBackupViewModel.CompressionName),
                    },
                    new HashSet<string?>(changed, StringComparer.Ordinal)
                )
        );
    }

    [Fact]
    internal async Task OnNavigatedToAsync_WhileAnOperationRuns_KeepsTheAlgorithmsTheRunStartedWith()
    {
        var sut = CreateSut();
        _ = this
            .settingsStore.LoadAsync<BackupCreationSettings>(Arg.Any<CancellationToken>())
            .Returns(
                new BackupCreationSettings(
                    EncryptionAlgorithm.Serpent,
                    KeyDerivationAlgorithm.Scrypt,
                    CompressionMode.Zstd
                )
            );

        var requests = StubCreateCapturingRequests();

        sut.IsRunning = true;
        await sut.OnNavigatedToAsync();
        sut.IsRunning = false;

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";
        sut.Password = "good-password";
        sut.ConfirmPassword = "good-password";

        await sut.StartCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.Single(requests),
            () => Assert.Equal(EncryptionAlgorithm.Aes, requests[0].EncryptionAlgorithm),
            () => Assert.Equal(KeyDerivationAlgorithm.Argon2id, requests[0].KeyDerivationAlgorithm),
            () => Assert.Equal(CompressionMode.None, requests[0].Compression)
        );
    }

    /// <summary>
    /// Renders a candidate for a failure message, replacing the long ones with their length so the
    /// report stays readable.
    /// </summary>
    /// <param name="candidate">The password or confirmation to describe.</param>
    /// <returns>The candidate itself, or a description of its length.</returns>
    private static string Describe(string candidate)
    {
        return candidate.Length <= 20 ? candidate : $"{candidate.Length} characters";
    }

    /// <summary>
    /// Builds the page with the settings reads and the strength analysis stubbed, since an unstubbed
    /// store hands the page a <see langword="null"/> settings object.
    /// </summary>
    /// <returns>The system under test.</returns>
    private CreateBackupViewModel CreateSut()
    {
        _ = this
            .settingsStore.LoadAsync<RecentPathSettings>(Arg.Any<CancellationToken>())
            .Returns(RecentPathSettings.DefaultValue);
        _ = this
            .settingsStore.SaveAsync(Arg.Any<RecentPathSettings>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _ = this
            .settingsStore.LoadAsync<BackupCreationSettings>(Arg.Any<CancellationToken>())
            .Returns(BackupCreationSettings.DefaultValue);
        _ = this.passwordService.AnalyzePasswordStrength(Arg.Any<string>())
            .Returns(new PasswordStrengthAnalysis(PasswordStrength.Good, 70, 72.5, []));

        return new CreateBackupViewModel(
            this.operations,
            this.settingsStore,
            this.filePicker,
            this.clipboardService,
            this.passwordService,
            this.algorithmCatalog
        );
    }

    /// <summary>
    /// Makes the create operation report a trivial success and records every request it receives.
    /// </summary>
    /// <returns>The list the captured requests are appended to.</returns>
    private List<CreateBackupRequest> StubCreateCapturingRequests()
    {
        List<CreateBackupRequest> requests = [];

        _ = this
            .operations.CreateAsync(
                Arg.Do<CreateBackupRequest>(requests.Add),
                Arg.Any<IProgress<BackupStatus>?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Task.FromResult(
                    Result<BackupOutcome>.Success(
                        BackupOutcome.Completed(
                            new BackupResult(TimeSpan.FromSeconds(1), 16, 1, 1)
                        )
                    )
                )
            );

        return requests;
    }
}
