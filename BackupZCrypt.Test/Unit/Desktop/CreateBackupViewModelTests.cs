using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.Validators.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Password;
using BackupZCrypt.Application.ValueObjects.Settings;
using BackupZCrypt.Desktop.Services;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Desktop.ViewModels;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Services.Interfaces;
using BackupZCrypt.Domain.ValueObjects.Backup;
using BackupZCrypt.Domain.ValueObjects.Localization;
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
    /// The substituted orchestrator the page dispatches the create request to.
    /// </summary>
    private readonly IBackupOrchestrator orchestrator = Substitute.For<IBackupOrchestrator>();

    /// <summary>
    /// The substituted settings service supplying the remembered paths and algorithm defaults.
    /// </summary>
    private readonly ISettingsService settingsService = Substitute.For<ISettingsService>();

    /// <summary>
    /// The substituted folder picker, never exercised here but required by the constructor.
    /// </summary>
    private readonly IFilePickerService filePicker = Substitute.For<IFilePickerService>();

    /// <summary>
    /// The substituted clipboard the generated password is copied to.
    /// </summary>
    private readonly IClipboardService clipboardService = Substitute.For<IClipboardService>();

    /// <summary>
    /// The substituted password service behind generation and the strength meter.
    /// </summary>
    private readonly IPasswordService passwordService = Substitute.For<IPasswordService>();

    [Test]
    public async Task StartCommand_CanExecute_AgreesWithTheRequestValidatorOnEveryPasswordRule()
    {
        using TempDir temp = new();
        _ = temp.WriteText(Path.Combine("source", "file.txt"), "content");
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
                new BackupRequest(
                    source,
                    destination,
                    password,
                    confirm,
                    EncryptionAlgorithm.Aes,
                    KeyDerivationAlgorithm.PBKDF2,
                    BackupOperation.Create
                )
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

        Assert.That(
            drift,
            Is.Empty,
            $"The start gate and the request validator disagree: {string.Join("; ", drift)}"
        );
    }

    [Test]
    public void GeneratePasswordCommand_FillsBothFieldsWithALongPasswordUsingEveryCharacterClass()
    {
        var sut = CreateSut();
        List<int> requestedLengths = [];
        List<PasswordGenerationOptions> requestedOptions = [];

        _ = this
            .passwordService.GeneratePassword(
                Arg.Do<int>(requestedLengths.Add),
                Arg.Do<PasswordGenerationOptions>(requestedOptions.Add)
            )
            .Returns("GENERATED-PASSWORD");

        sut.RevealPassword = true;
        sut.ConfirmPassword = "stale-confirmation";

        sut.GeneratePasswordCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(requestedLengths, Is.EqualTo([50]));
            Assert.That(
                requestedOptions,
                Is.EqualTo(
                    [
                        PasswordGenerationOptions.IncludeUppercase
                            | PasswordGenerationOptions.IncludeLowercase
                            | PasswordGenerationOptions.IncludeNumbers
                            | PasswordGenerationOptions.IncludeSpecialCharacters,
                    ]
                )
            );
            Assert.That(sut.Password, Is.EqualTo("GENERATED-PASSWORD"));
            Assert.That(sut.ConfirmPassword, Is.EqualTo("GENERATED-PASSWORD"));
            Assert.That(sut.RevealPassword, Is.False);
            Assert.That(sut.ShowPasswordMismatch, Is.False);
        }
    }

    [Test]
    public async Task CopyPasswordCommand_IsGatedOnAPasswordAndCopiesItVerbatim()
    {
        var sut = CreateSut();
        List<string> copied = [];
        _ = this.clipboardService.SetTextAsync(Arg.Do<string>(copied.Add))
            .Returns(Task.CompletedTask);

        var enabledWithoutPassword = sut.CopyPasswordCommand.CanExecute(null);

        sut.Password = "  pass word  ";
        var enabledWithPassword = sut.CopyPasswordCommand.CanExecute(null);

        await sut.CopyPasswordCommand.ExecuteAsync(null);

        sut.Password = string.Empty;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(enabledWithoutPassword, Is.False);
            Assert.That(enabledWithPassword, Is.True);
            Assert.That(copied, Is.EqualTo(["  pass word  "]));
            Assert.That(sut.CopyPasswordCommand.CanExecute(null), Is.False);
        }
    }

    [Test]
    public void Password_WhenSetAndThenCleared_PublishesAndThenClearsTheStrengthFeedback()
    {
        var sut = CreateSut();
        PasswordStrengthAnalysis analysis = new(
            PasswordStrength.Weak,
            22,
            31.4,
            [MessageCode.TipIncreaseLength]
        );
        _ = this.passwordService.AnalyzePasswordStrength("weak-password").Returns(analysis);

        sut.Password = "weak-password";
        var hasStrength = sut.HasStrength;
        var score = sut.StrengthScore;
        var description = sut.StrengthDescription;

        sut.Password = string.Empty;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hasStrength, Is.True);
            Assert.That(score, Is.EqualTo(22));
            Assert.That(description, Is.EqualTo(PasswordStrengthFormatter.Format(analysis)));
            Assert.That(sut.HasStrength, Is.False);
            Assert.That(sut.StrengthScore, Is.Zero);
            Assert.That(sut.StrengthDescription, Is.Empty);
        }
    }

    [TestCase("abcdefgh", "", false)]
    [TestCase("abcdefgh", "abc", true)]
    [TestCase("abcdefgh", "abcdefgh", false)]
    public void ConfirmPassword_WhenTyped_ShowsTheMismatchHintOnlyOnceSomethingWasEntered(
        string password,
        string confirm,
        bool expected
    )
    {
        var sut = CreateSut();

        sut.Password = password;
        sut.ConfirmPassword = confirm;

        Assert.That(sut.ShowPasswordMismatch, Is.EqualTo(expected));
    }

    [Test]
    public async Task OnNavigatedToAsync_WithAnUndefinedStoredEncryptionAlgorithm_FallsBackToAes()
    {
        var sut = CreateSut();
        _ = this
            .settingsService.GetOrCreateAsync<BackupCreationSettings>(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    new BackupCreationSettings(
                        (EncryptionAlgorithm)99,
                        KeyDerivationAlgorithm.Scrypt,
                        CompressionMode.ZstdBest
                    )
                )
            );

        var requests = StubOrchestratorCapturingRequests();

        await sut.OnNavigatedToAsync();

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";
        sut.Password = "good-password";
        sut.ConfirmPassword = "good-password";

        await sut.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(requests, Has.Count.EqualTo(1));
            Assert.That(requests[0].EncryptionAlgorithm, Is.EqualTo(EncryptionAlgorithm.Aes));
            Assert.That(
                requests[0].KeyDerivationAlgorithm,
                Is.EqualTo(KeyDerivationAlgorithm.Scrypt)
            );
            Assert.That(requests[0].Compression, Is.EqualTo(CompressionMode.ZstdBest));
            Assert.That(requests[0].Operation, Is.EqualTo(BackupOperation.Create));
            Assert.That(requests[0].Password, Is.EqualTo("good-password"));
            Assert.That(requests[0].ConfirmPassword, Is.EqualTo("good-password"));
            Assert.That(requests[0].ProceedOnWarnings, Is.False);
        }
    }

    [Test]
    public async Task OnNavigatedToAsync_WhileAnOperationRuns_KeepsTheAlgorithmsTheRunStartedWith()
    {
        var sut = CreateSut();
        _ = this
            .settingsService.GetOrCreateAsync<BackupCreationSettings>(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    new BackupCreationSettings(
                        EncryptionAlgorithm.Serpent,
                        KeyDerivationAlgorithm.Scrypt,
                        CompressionMode.Zstd
                    )
                )
            );

        var requests = StubOrchestratorCapturingRequests();

        sut.IsRunning = true;
        await sut.OnNavigatedToAsync();
        sut.IsRunning = false;

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";
        sut.Password = "good-password";
        sut.ConfirmPassword = "good-password";

        await sut.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(requests, Has.Count.EqualTo(1));
            Assert.That(requests[0].EncryptionAlgorithm, Is.EqualTo(EncryptionAlgorithm.Aes));
            Assert.That(
                requests[0].KeyDerivationAlgorithm,
                Is.EqualTo(KeyDerivationAlgorithm.Argon2id)
            );
            Assert.That(requests[0].Compression, Is.EqualTo(CompressionMode.None));
        }
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
    /// settings read hands the page a <see langword="null"/> settings object.
    /// </summary>
    /// <returns>The system under test.</returns>
    private CreateBackupViewModel CreateSut()
    {
        _ = this
            .settingsService.GetOrCreateAsync<RecentPathSettings>(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(RecentPathSettings.DefaultValue));
        _ = this
            .settingsService.GetOrCreateAsync<BackupCreationSettings>(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BackupCreationSettings.DefaultValue));
        _ = this.passwordService.AnalyzePasswordStrength(Arg.Any<string>())
            .Returns(new PasswordStrengthAnalysis(PasswordStrength.Good, 70, 72.5, []));

        return new CreateBackupViewModel(
            this.orchestrator,
            this.settingsService,
            this.filePicker,
            this.clipboardService,
            this.passwordService
        );
    }

    /// <summary>
    /// Makes the orchestrator report a trivial success and records every request it receives.
    /// </summary>
    /// <returns>The list the captured requests are appended to.</returns>
    private List<BackupRequest> StubOrchestratorCapturingRequests()
    {
        List<BackupRequest> requests = [];

        _ = this
            .orchestrator.ExecuteAsync(
                Arg.Do<BackupRequest>(requests.Add),
                Arg.Any<IProgress<BackupStatus>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Task.FromResult(
                    Result<BackupResult>.Success(
                        new BackupResult(true, TimeSpan.FromSeconds(1), 16, 1, 1)
                    )
                )
            );

        return requests;
    }
}
