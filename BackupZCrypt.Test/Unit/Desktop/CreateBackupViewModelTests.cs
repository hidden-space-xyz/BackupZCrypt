using BackupZCrypt.Application.Commands;
using BackupZCrypt.Application.Commands.Interfaces;
using BackupZCrypt.Application.Queries;
using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.Validators.Interfaces;
using BackupZCrypt.Application.ValueObjects;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Application.ValueObjects.Password;
using BackupZCrypt.Application.ValueObjects.Settings;
using BackupZCrypt.Desktop.Services;
using BackupZCrypt.Desktop.Services.Interfaces;
using BackupZCrypt.Desktop.ViewModels;
using BackupZCrypt.Domain.Enums;
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
    /// The substituted handler the page dispatches the create command to.
    /// </summary>
    private readonly ICommandHandler<CreateBackupCommand, Result<BackupOutcome>> createBackup =
        Substitute.For<ICommandHandler<CreateBackupCommand, Result<BackupOutcome>>>();

    /// <summary>
    /// The substituted handler supplying the remembered paths.
    /// </summary>
    private readonly IQueryHandler<GetSettingsQuery<RecentPathSettings>, RecentPathSettings> recentPathsQuery =
        Substitute.For<IQueryHandler<GetSettingsQuery<RecentPathSettings>, RecentPathSettings>>();

    /// <summary>
    /// The substituted handler the page persists the recent paths through.
    /// </summary>
    private readonly ICommandHandler<SaveSettingsCommand<RecentPathSettings>, Result> saveRecentPaths =
        Substitute.For<ICommandHandler<SaveSettingsCommand<RecentPathSettings>, Result>>();

    /// <summary>
    /// The substituted handler supplying the saved algorithm defaults.
    /// </summary>
    private readonly IQueryHandler<GetSettingsQuery<BackupCreationSettings>, BackupCreationSettings> creationDefaultsQuery =
        Substitute.For<IQueryHandler<GetSettingsQuery<BackupCreationSettings>, BackupCreationSettings>>();

    /// <summary>
    /// The substituted folder picker, never exercised here but required by the constructor.
    /// </summary>
    private readonly IFilePickerService filePicker = Substitute.For<IFilePickerService>();

    /// <summary>
    /// The substituted clipboard the generated password is copied to.
    /// </summary>
    private readonly IClipboardService clipboardService = Substitute.For<IClipboardService>();

    /// <summary>
    /// The substituted handler behind password generation.
    /// </summary>
    private readonly ISyncQueryHandler<GeneratePasswordQuery, string> generatePassword =
        Substitute.For<ISyncQueryHandler<GeneratePasswordQuery, string>>();

    /// <summary>
    /// The substituted handler behind the strength meter.
    /// </summary>
    private readonly ISyncQueryHandler<AnalyzePasswordStrengthQuery, PasswordStrengthAnalysis> analyzePasswordStrength =
        Substitute.For<ISyncQueryHandler<AnalyzePasswordStrengthQuery, PasswordStrengthAnalysis>>();

    [Fact]
    internal async Task StartCommand_CanExecute_AgreesWithTheRequestValidatorOnEveryPasswordRule()
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
        List<GeneratePasswordQuery> queries = [];

        _ = this
            .generatePassword.Handle(Arg.Do<GeneratePasswordQuery>(queries.Add))
            .Returns("GENERATED-PASSWORD");

        sut.RevealPassword = true;
        sut.ConfirmPassword = "stale-confirmation";

        sut.GeneratePasswordCommand.Execute(null);

        GeneratePasswordQuery[] expectedQueries =
        [
            new GeneratePasswordQuery(
                50,
                PasswordGenerationOptions.IncludeUppercase
                    | PasswordGenerationOptions.IncludeLowercase
                    | PasswordGenerationOptions.IncludeNumbers
                    | PasswordGenerationOptions.IncludeSpecialCharacters
            ),
        ];

        Assert.Multiple(
            () => Assert.Equal(expectedQueries, queries),
            () => Assert.Equal("GENERATED-PASSWORD", sut.Password),
            () => Assert.Equal("GENERATED-PASSWORD", sut.ConfirmPassword),
            () => Assert.False(sut.RevealPassword),
            () => Assert.False(sut.ShowPasswordMismatch)
        );
    }

    [Fact]
    internal async Task CopyPasswordCommand_IsGatedOnAPasswordAndCopiesItVerbatim()
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
            .analyzePasswordStrength.Handle(new AnalyzePasswordStrengthQuery("weak-password"))
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
            .creationDefaultsQuery.HandleAsync(
                Arg.Any<GetSettingsQuery<BackupCreationSettings>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new BackupCreationSettings(
                    (EncryptionAlgorithm)99,
                    KeyDerivationAlgorithm.Scrypt,
                    CompressionMode.ZstdBest
                )
            );

        var commands = StubCreateCapturingCommands();

        await sut.OnNavigatedToAsync();

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";
        sut.Password = "good-password";
        sut.ConfirmPassword = "good-password";

        await sut.StartCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.Single(commands),
            () => Assert.Equal(EncryptionAlgorithm.Aes, commands[0].EncryptionAlgorithm),
            () => Assert.Equal(KeyDerivationAlgorithm.Scrypt, commands[0].KeyDerivationAlgorithm),
            () => Assert.Equal(CompressionMode.ZstdBest, commands[0].Compression),
            () => Assert.Equal("good-password", commands[0].Password),
            () => Assert.Equal("good-password", commands[0].ConfirmPassword),
            () => Assert.False(commands[0].ProceedOnWarnings)
        );
    }

    [Fact]
    internal async Task OnNavigatedToAsync_WhileAnOperationRuns_KeepsTheAlgorithmsTheRunStartedWith()
    {
        var sut = CreateSut();
        _ = this
            .creationDefaultsQuery.HandleAsync(
                Arg.Any<GetSettingsQuery<BackupCreationSettings>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new BackupCreationSettings(
                    EncryptionAlgorithm.Serpent,
                    KeyDerivationAlgorithm.Scrypt,
                    CompressionMode.Zstd
                )
            );

        var commands = StubCreateCapturingCommands();

        sut.IsRunning = true;
        await sut.OnNavigatedToAsync();
        sut.IsRunning = false;

        sut.SourcePath = "source";
        sut.DestinationPath = "destination";
        sut.Password = "good-password";
        sut.ConfirmPassword = "good-password";

        await sut.StartCommand.ExecuteAsync(null);

        Assert.Multiple(
            () => Assert.Single(commands),
            () => Assert.Equal(EncryptionAlgorithm.Aes, commands[0].EncryptionAlgorithm),
            () => Assert.Equal(KeyDerivationAlgorithm.Argon2id, commands[0].KeyDerivationAlgorithm),
            () => Assert.Equal(CompressionMode.None, commands[0].Compression)
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
    /// handler hands the page a <see langword="null"/> settings object.
    /// </summary>
    /// <returns>The system under test.</returns>
    private CreateBackupViewModel CreateSut()
    {
        _ = this
            .recentPathsQuery.HandleAsync(
                Arg.Any<GetSettingsQuery<RecentPathSettings>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(RecentPathSettings.DefaultValue);
        _ = this
            .saveRecentPaths.HandleAsync(
                Arg.Any<SaveSettingsCommand<RecentPathSettings>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        _ = this
            .creationDefaultsQuery.HandleAsync(
                Arg.Any<GetSettingsQuery<BackupCreationSettings>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(BackupCreationSettings.DefaultValue);
        _ = this.analyzePasswordStrength.Handle(Arg.Any<AnalyzePasswordStrengthQuery>())
            .Returns(new PasswordStrengthAnalysis(PasswordStrength.Good, 70, 72.5, []));

        return new CreateBackupViewModel(
            this.createBackup,
            this.recentPathsQuery,
            this.saveRecentPaths,
            this.creationDefaultsQuery,
            this.filePicker,
            this.clipboardService,
            this.generatePassword,
            this.analyzePasswordStrength
        );
    }

    /// <summary>
    /// Makes the create handler report a trivial success and records every command it receives.
    /// </summary>
    /// <returns>The list the captured commands are appended to.</returns>
    private List<CreateBackupCommand> StubCreateCapturingCommands()
    {
        List<CreateBackupCommand> commands = [];

        _ = this
            .createBackup.HandleAsync(
                Arg.Do<CreateBackupCommand>(commands.Add),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Task.FromResult(
                    Result<BackupOutcome>.Success(
                        BackupOutcome.Completed(
                            new BackupResult(true, TimeSpan.FromSeconds(1), 16, 1, 1)
                        )
                    )
                )
            );

        return commands;
    }
}
