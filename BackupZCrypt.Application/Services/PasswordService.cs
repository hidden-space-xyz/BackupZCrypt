using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Password;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Application.Services;

/// <summary>
/// Estimates password strength from character-pool entropy with penalties for repetition,
/// sequences, common substrings, and years, and generates random passwords from selectable
/// character classes using a cryptographic RNG.
/// </summary>
internal sealed partial class PasswordService : IPasswordService
{
    /// <summary>
    /// The effective entropy, in bits, that is treated as a perfect password; anything at or above it scores
    /// the maximum. Roughly the strength of a 20-character mixed-class password.
    /// </summary>
    private const double MaxEntropyBits = 120.0;

    /// <summary>
    /// The upper bound of the strength score scale reported to the user.
    /// </summary>
    private const double MaxScore = 100.0;

    /// <summary>
    /// The number of distinct character classes a password must use before it can earn the bonus points.
    /// </summary>
    private const int MinCategoriesForBonus = 4;

    /// <summary>
    /// The password length required before the bonus points can be earned.
    /// </summary>
    private const int MinLengthForBonus = 16;

    /// <summary>
    /// The effective entropy, in bits, required before the bonus points can be earned.
    /// </summary>
    private const double MinEntropyForBonus = 90.0;

    /// <summary>
    /// The points added to the score when a password satisfies the length, variety, and entropy conditions
    /// together, rewarding passwords that are strong on every axis at once.
    /// </summary>
    private const double BonusPoints = 5.0;

    /// <summary>
    /// The number of symbols one letter case contributes to the assumed search alphabet.
    /// </summary>
    private const int LetterPoolSize = 26;

    /// <summary>
    /// The number of symbols the decimal digits contribute to the assumed search alphabet.
    /// </summary>
    private const int DigitPoolSize = 10;

    /// <summary>
    /// The number of symbols the printable punctuation set contributes to the assumed search alphabet.
    /// </summary>
    private const int SpecialCharPoolSize = 32;

    /// <summary>
    /// The number of symbols credited when any non-ASCII character is present, kept deliberately conservative
    /// because the real Unicode alphabet an attacker would search is unknowable.
    /// </summary>
    private const int UnicodePoolSize = 50;

    /// <summary>
    /// The lowest score rated as <see cref="PasswordStrength.Strong"/>.
    /// </summary>
    private const double StrongThreshold = 85.0;

    /// <summary>
    /// The lowest score rated as <see cref="PasswordStrength.Good"/>.
    /// </summary>
    private const double GoodThreshold = 65.0;

    /// <summary>
    /// The lowest score rated as <see cref="PasswordStrength.Fair"/>.
    /// </summary>
    private const double FairThreshold = 45.0;

    /// <summary>
    /// The lowest score rated as <see cref="PasswordStrength.Weak"/>; anything below is
    /// <see cref="PasswordStrength.VeryWeak"/>.
    /// </summary>
    private const double WeakThreshold = 25.0;

    /// <summary>
    /// The uppercase letters the generator draws from.
    /// </summary>
    private const string UppercaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>
    /// The lowercase letters the generator draws from.
    /// </summary>
    private const string LowercaseChars = "abcdefghijklmnopqrstuvwxyz";

    /// <summary>
    /// The digits the generator draws from.
    /// </summary>
    private const string NumberChars = "0123456789";

    /// <summary>
    /// The punctuation the generator draws from.
    /// </summary>
    private const string SpecialChars = "!@#$%^&*()-_=+[]{}|;:,.<>?";

    /// <summary>
    /// The characters that are easy to confuse when read back by eye, removed from the generator alphabet when
    /// <see cref="PasswordGenerationOptions.ExcludeSimilarCharacters"/> is set.
    /// </summary>
    private const string SimilarChars = "il1Lo0O";

    /// <summary>
    /// The match timeout applied to every regex in this class.
    /// </summary>
    /// <remarks>
    /// None of these patterns can backtrack catastrophically and every input is a length-bounded
    /// password, so the timeout is never expected to elapse. It is set anyway because these patterns
    /// are the only ones in the solution that run over attacker-supplied text, and an unbounded match
    /// is the shape of a denial-of-service bug even when today's pattern happens to be linear.
    /// </remarks>
    private const int RegexTimeoutMilliseconds = 1000;

    /// <summary>
    /// The regex that detects an ASCII uppercase letter.
    /// </summary>
    private static readonly Regex UpperCaseRegex = UpperCaseRegexFactory();

    /// <summary>
    /// The regex that detects an ASCII lowercase letter.
    /// </summary>
    private static readonly Regex LowerCaseRegex = LowerCaseRegexFactory();

    /// <summary>
    /// The regex that detects a decimal digit.
    /// </summary>
    private static readonly Regex NumberRegex = NumberRegexFactory();

    /// <summary>
    /// The regex that detects a recognized punctuation character.
    /// </summary>
    private static readonly Regex SpecialCharRegex = SpecialCharRegexFactory();

    /// <summary>
    /// The regex that matches a standalone four-digit year in the 1900-2099 range, since birth and current
    /// years are among the most guessable substrings a password can contain.
    /// </summary>
    private static readonly Regex YearRegex = YearRegexFactory();

    /// <summary>
    /// The well-known words and keyboard runs that each cost a fixed entropy penalty when found in a password.
    /// </summary>
    private static readonly string[] CommonSubstrings =
    [
        "password",
        "qwerty",
        "admin",
        "user",
        "login",
        "test",
        "guest",
        "root",
        "abc",
        "qwe",
        "letmein",
    ];

    /// <summary>
    /// The alphabet, keyboard rows, and digit run that passwords are scanned against, in both directions, to
    /// find predictable ascending or descending runs.
    /// </summary>
    private static readonly string[] LinearSequences =
    [
        "abcdefghijklmnopqrstuvwxyz",
        "qwertyuiop",
        "asdfghjkl",
        "zxcvbnm",
        "0123456789",
    ];

    /// <summary>
    /// The leetspeak substitutions folded back to the letters they imitate, so that disguised common words such
    /// as <c>p@ssw0rd</c> are still penalized.
    /// </summary>
    private static readonly Dictionary<char, char> LeetMap = new()
    {
        ['0'] = 'o',
        ['1'] = 'l',
        ['3'] = 'e',
        ['4'] = 'a',
        ['5'] = 's',
        ['7'] = 't',
        ['8'] = 'b',
        ['9'] = 'g',
        ['@'] = 'a',
        ['$'] = 's',
        ['!'] = 'i',
    };

    /// <summary>
    /// Evaluates a password's effective entropy and maps it to a strength rating, score, and improvement tips.
    /// </summary>
    /// <param name="password">The password to analyze; leading and trailing whitespace is ignored.</param>
    /// <returns>An analysis containing the strength rating, score, entropy, and localizable tips.</returns>
    public PasswordStrengthAnalysis AnalyzePasswordStrength(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return new PasswordStrengthAnalysis(PasswordStrength.VeryWeak, 0, 0, []);
        }

        var trimmed = password.Trim();
        var poolSize = EstimatePoolSize(trimmed, out var compositionFlags);
        var baseEntropy = poolSize > 1 ? trimmed.Length * Math.Log2(poolSize) : 0;
        double penaltyBits = 0;

        penaltyBits += RepetitionPenalty(trimmed);
        penaltyBits += SequencePenalty(trimmed);
        penaltyBits += PatternPenalty(trimmed);
        penaltyBits += YearPenalty(trimmed);
        penaltyBits += HomogeneousClassPenalty(compositionFlags, trimmed);

        var entropy = Math.Max(0, baseEntropy - penaltyBits);
        var rawScore = entropy / MaxEntropyBits * MaxScore;
        var score = Math.Max(0, Math.Min(MaxScore, rawScore));

        if (
            score < MaxScore
            && compositionFlags.CategoryCount >= MinCategoriesForBonus
            && trimmed.Length >= MinLengthForBonus
            && entropy >= MinEntropyForBonus
        )
        {
            score = Math.Min(MaxScore, score + BonusPoints);
        }

        var strength = GetStrengthFromScore(score);
        var tips = BuildTips(compositionFlags, trimmed);

        return new PasswordStrengthAnalysis(strength, Math.Round(score, 2), entropy, tips);
    }

    /// <summary>
    /// Generates a random password of the given length using rejection sampling over the selected
    /// character classes to avoid modulo bias.
    /// </summary>
    /// <param name="length">The number of characters to generate; must be positive.</param>
    /// <param name="options">The flags selecting which character classes to include and exclusions to apply.</param>
    /// <returns>A cryptographically random password.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is zero or negative.</exception>
    /// <exception cref="ArgumentException">No character class is selected, or the resulting character set is empty.</exception>
    public string GeneratePassword(int length, PasswordGenerationOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        if (options == PasswordGenerationOptions.None)
        {
            throw new ArgumentException(
                "At least one character type must be selected.",
                nameof(options)
            );
        }

        StringBuilder charSet = new();

        if (options.HasFlag(PasswordGenerationOptions.IncludeUppercase))
        {
            _ = charSet.Append(UppercaseChars);
        }

        if (options.HasFlag(PasswordGenerationOptions.IncludeLowercase))
        {
            _ = charSet.Append(LowercaseChars);
        }

        if (options.HasFlag(PasswordGenerationOptions.IncludeNumbers))
        {
            _ = charSet.Append(NumberChars);
        }

        if (options.HasFlag(PasswordGenerationOptions.IncludeSpecialCharacters))
        {
            _ = charSet.Append(SpecialChars);
        }

        var availableChars = charSet.ToString();

        if (options.HasFlag(PasswordGenerationOptions.ExcludeSimilarCharacters))
        {
            availableChars = new string([
                .. availableChars.Where(c => !SimilarChars.Contains(c, StringComparison.Ordinal)),
            ]);
        }

        if (string.IsNullOrEmpty(availableChars))
        {
            throw new ArgumentException(
                "No characters available for password generation with the given options.",
                nameof(options)
            );
        }

        StringBuilder password = new(length);

        var charCount = availableChars.Length;
        var maxValidByte = 256 - (256 % charCount);
        var randomBytes = new byte[Math.Max(length * 2, 64)];
        var bufferIndex = randomBytes.Length;

        try
        {
            for (var i = 0; i < length;)
            {
                if (bufferIndex >= randomBytes.Length)
                {
                    RandomNumberGenerator.Fill(randomBytes);
                    bufferIndex = 0;
                }

                var value = randomBytes[bufferIndex++];

                if (value < maxValidByte)
                {
                    _ = password.Append(availableChars[value % charCount]);
                    i++;
                }
            }

            return password.ToString();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(randomBytes);
        }
    }

    /// <summary>
    /// Sums the alphabet an attacker would have to search, adding one pool per character class the password
    /// actually uses, and reports which classes were found.
    /// </summary>
    /// <param name="password">The password to inspect.</param>
    /// <param name="flags">Receives the character classes detected in the password.</param>
    /// <returns>The estimated size of the search alphabet, in symbols.</returns>
    private static int EstimatePoolSize(string password, out PasswordComposition flags)
    {
        var hasUpper = UpperCaseRegex.IsMatch(password);
        var hasLower = LowerCaseRegex.IsMatch(password);
        var hasDigit = NumberRegex.IsMatch(password);
        var hasSpecial = SpecialCharRegex.IsMatch(password);
        var hasOther = password.Any(c => c > 127);

        var size = 0;

        if (hasLower)
        {
            size += LetterPoolSize;
        }

        if (hasUpper)
        {
            size += LetterPoolSize;
        }

        if (hasDigit)
        {
            size += DigitPoolSize;
        }

        if (hasSpecial)
        {
            size += SpecialCharPoolSize;
        }

        if (hasOther)
        {
            size += UnicodePoolSize;
        }

        flags = new PasswordComposition(hasUpper, hasLower, hasDigit, hasSpecial, hasOther);

        return size;
    }

    /// <summary>
    /// Computes the entropy penalty for runs of three or more identical adjacent characters, charging 1.5 bits
    /// for every character beyond the second in each run.
    /// </summary>
    /// <param name="password">The password to inspect.</param>
    /// <returns>The penalty in bits.</returns>
    private static double RepetitionPenalty(string password)
    {
        double penalty = 0;
        var runLength = 1;

        for (var i = 1; i < password.Length; i++)
        {
            if (password[i] == password[i - 1])
            {
                runLength++;
            }
            else
            {
                if (runLength > 2)
                {
                    penalty += (runLength - 2) * 1.5;
                }

                runLength = 1;
            }
        }

        if (runLength > 2)
        {
            penalty += (runLength - 2) * 1.5;
        }

        return penalty;
    }

    /// <summary>
    /// Computes the entropy penalty for runs drawn from the known linear sequences, scanning each sequence both
    /// forwards and reversed.
    /// </summary>
    /// <param name="password">The password to inspect.</param>
    /// <returns>The penalty in bits.</returns>
    private static double SequencePenalty(string password)
    {
        double penalty = 0;
        var lower = password.ToLowerInvariant();

        foreach (var seq in LinearSequences)
        {
            penalty += SequenceScan(lower, seq);
            string rev = new([.. seq.Reverse()]);
            penalty += SequenceScan(lower, rev);
        }

        return penalty;
    }

    /// <summary>
    /// Charges 2 bits for every character beyond the second at each position where the password begins to track
    /// the given sequence for at least three characters.
    /// </summary>
    /// <param name="passwordLower">The password lowercased for comparison.</param>
    /// <param name="sequence">The sequence to match against, starting at its first character.</param>
    /// <returns>The penalty in bits.</returns>
    private static double SequenceScan(string passwordLower, string sequence)
    {
        double penalty = 0;

        for (var i = 0; i <= passwordLower.Length - 3; i++)
        {
            var max = Math.Min(sequence.Length, passwordLower.Length - i);
            var len = 0;

            for (var j = 0; j < max; j++)
            {
                if (passwordLower[i + j] == sequence[j])
                {
                    len++;
                }
                else
                {
                    break;
                }
            }

            if (len >= 3)
            {
                penalty += (len - 2) * 2.0;
            }
        }

        return penalty;
    }

    /// <summary>
    /// Charges 6 bits for each common substring found in the password, counting it once literally and again
    /// after leetspeak normalization so a substitution cipher does not hide it.
    /// </summary>
    /// <param name="password">The password to inspect.</param>
    /// <returns>The penalty in bits.</returns>
    private static double PatternPenalty(string password)
    {
        var lower = password.ToLowerInvariant();

        double penalty = CommonSubstrings.Where(lower.Contains).Sum(_ => 6);
        var canon = NormalizeLeet(lower);
        penalty += CommonSubstrings.Where(canon.Contains).Sum(_ => 6);

        return penalty;
    }

    /// <summary>
    /// Charges 4 bits for every four-digit year the password contains.
    /// </summary>
    /// <param name="password">The password to inspect.</param>
    /// <returns>The penalty in bits.</returns>
    private static double YearPenalty(string password)
    {
        return YearRegex.Count(password) * 4.0;
    }

    /// <summary>
    /// Penalizes passwords drawn from too few character classes: a single-class password loses 2 bits per
    /// character up to 20, and a short two-class password loses a flat 10.
    /// </summary>
    /// <param name="flags">The character classes detected in the password.</param>
    /// <param name="password">The password to inspect.</param>
    /// <returns>The penalty in bits.</returns>
    private static double HomogeneousClassPenalty(PasswordComposition flags, string password)
    {
        if (flags.CategoryCount <= 1)
        {
            return Math.Min(20, password.Length * 2);
        }

        return flags.CategoryCount == 2 && password.Length < 10 ? 10 : 0;
    }

    /// <summary>
    /// Replaces leetspeak characters with the letters they imitate, leaving every other character untouched.
    /// </summary>
    /// <param name="input">The lowercased password to normalize.</param>
    /// <returns>The normalized text.</returns>
    private static string NormalizeLeet(string input)
    {
        StringBuilder sb = new(input.Length);

        foreach (var c in input)
        {
            if (LeetMap.TryGetValue(c, out var mapped))
            {
                _ = sb.Append(mapped);
            }
            else
            {
                _ = sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Maps a numeric score onto the coarse strength rating shown to the user.
    /// </summary>
    /// <param name="score">The computed strength score.</param>
    /// <returns>The rating the score falls into.</returns>
    private static PasswordStrength GetStrengthFromScore(double score)
    {
        return score switch
        {
            >= StrongThreshold => PasswordStrength.Strong,
            >= GoodThreshold => PasswordStrength.Good,
            >= FairThreshold => PasswordStrength.Fair,
            >= WeakThreshold => PasswordStrength.Weak,
            _ => PasswordStrength.VeryWeak,
        };
    }

    /// <summary>
    /// Selects the message codes for the improvement advice that applies to this password, which the Desktop
    /// layer resolves into translated text.
    /// </summary>
    /// <param name="flags">The character classes detected in the password.</param>
    /// <param name="password">The password the advice is about.</param>
    /// <returns>The applicable tips, in the order they are shown.</returns>
    private static List<MessageCode> BuildTips(PasswordComposition flags, string password)
    {
        List<MessageCode> tips = [];

        if (password.Length < 12)
        {
            tips.Add(MessageCode.TipIncreaseLength);
        }

        if (!flags.HasUpper)
        {
            tips.Add(MessageCode.TipAddUppercase);
        }

        if (!flags.HasLower)
        {
            tips.Add(MessageCode.TipAddLowercase);
        }

        if (!flags.HasDigit)
        {
            tips.Add(MessageCode.TipAddDigits);
        }

        if (!flags.HasSpecial)
        {
            tips.Add(MessageCode.TipAddSymbols);
        }

        if (flags.CategoryCount < 4 && password.Length < 16)
        {
            tips.Add(MessageCode.TipMoreVariety);
        }

        if (HasObviousSequence(password))
        {
            tips.Add(MessageCode.TipAvoidSequences);
        }

        if (HasRepeats(password))
        {
            tips.Add(MessageCode.TipReduceRepeats);
        }

        if (YearRegex.IsMatch(password))
        {
            tips.Add(MessageCode.TipAvoidYears);
        }

        return tips;
    }

    /// <summary>
    /// Determines whether the password contains the first four characters of a known linear sequence, read
    /// either forwards or backwards.
    /// </summary>
    /// <param name="password">The password to inspect.</param>
    /// <returns><see langword="true"/> if an obvious sequence is present; otherwise <see langword="false"/>.</returns>
    private static bool HasObviousSequence(string password)
    {
        var lower = password.ToLowerInvariant();

        return LinearSequences.Any(seq =>
                lower.Contains(seq[..Math.Min(seq.Length, 4)], StringComparison.Ordinal)
            )
            || LinearSequences.Any(seq =>
            {
                string rev = new([.. seq.Reverse()]);
                return lower.Contains(rev[..Math.Min(rev.Length, 4)], StringComparison.Ordinal);
            });
    }

    /// <summary>
    /// Determines whether the password contains two identical adjacent characters.
    /// </summary>
    /// <param name="password">The password to inspect.</param>
    /// <returns><see langword="true"/> if any character repeats immediately; otherwise <see langword="false"/>.</returns>
    private static bool HasRepeats(string password)
    {
        for (var i = 1; i < password.Length; i++)
        {
            if (password[i] == password[i - 1])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds the source-generated regex matching a single ASCII uppercase letter.
    /// </summary>
    /// <returns>The generated regex.</returns>
    [GeneratedRegex("[A-Z]", RegexOptions.None, RegexTimeoutMilliseconds)]
    private static partial Regex UpperCaseRegexFactory();

    /// <summary>
    /// Builds the source-generated regex matching a single ASCII lowercase letter.
    /// </summary>
    /// <returns>The generated regex.</returns>
    [GeneratedRegex("[a-z]", RegexOptions.None, RegexTimeoutMilliseconds)]
    private static partial Regex LowerCaseRegexFactory();

    /// <summary>
    /// Builds the source-generated regex matching a single decimal digit.
    /// </summary>
    /// <returns>The generated regex.</returns>
    [GeneratedRegex("[0-9]", RegexOptions.None, RegexTimeoutMilliseconds)]
    private static partial Regex NumberRegexFactory();

    /// <summary>
    /// Builds the source-generated regex matching a single recognized punctuation character.
    /// </summary>
    /// <returns>The generated regex.</returns>
    [GeneratedRegex(@"[!@#$%^&*()_+\-=\[\]{};':""\\|,.<>\/?]", RegexOptions.None, RegexTimeoutMilliseconds)]
    private static partial Regex SpecialCharRegexFactory();

    /// <summary>
    /// Builds the source-generated regex matching a standalone four-digit year between 1900 and 2099.
    /// </summary>
    /// <returns>The generated regex.</returns>
    [GeneratedRegex(@"\b(?:19|20)\d{2}\b", RegexOptions.None, RegexTimeoutMilliseconds)]
    private static partial Regex YearRegexFactory();
}
