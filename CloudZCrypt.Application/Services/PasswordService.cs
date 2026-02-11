using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CloudZCrypt.Application.Resources;
using CloudZCrypt.Application.Services.Interfaces;
using CloudZCrypt.Application.ValueObjects.Password;
using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Exceptions;

namespace CloudZCrypt.Application.Services
{
    internal class PasswordService : IPasswordService
    {
        private const double MaxEntropyBits = 120.0;
        private const string UppercaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const string LowercaseChars = "abcdefghijklmnopqrstuvwxyz";
        private const string NumberChars = "0123456789";
        private const string SpecialChars = "!@#$%^&*()-_=+[]{}|;:,.<>?";
        private const string SimilarChars = "il1Lo0O";

        private static readonly Regex UpperCaseRegex = new(@"[A-Z]");
        private static readonly Regex LowerCaseRegex = new(@"[a-z]");
        private static readonly Regex NumberRegex = new(@"[0-9]");
        private static readonly Regex SpecialCharRegex = new(
            @"[!@#$%^&*()_+\-=\[\]{};':""\\|,.<>\/?]"
        );
        private static readonly Regex YearRegex = new(@"\b(19|20)\d{2}\b");

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

        private static readonly string[] LinearSequences =
        [
            "abcdefghijklmnopqrstuvwxyz",
            "qwertyuiop",
            "asdfghjkl",
            "zxcvbnm",
            "0123456789",
        ];

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

        public PasswordStrengthAnalysis AnalyzePasswordStrength(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                return new PasswordStrengthAnalysis(
                    PasswordStrength.VeryWeak,
                    Messages.EmptyPasswordDescription,
                    0
                );
            }

            string trimmed = password.Trim();
            int poolSize = EstimatePoolSize(trimmed, out PasswordComposition compositionFlags);
            double baseEntropy = poolSize > 1 ? trimmed.Length * Math.Log2(poolSize) : 0;
            double penaltyBits = 0;

            penaltyBits += RepetitionPenalty(trimmed);
            penaltyBits += SequencePenalty(trimmed);
            penaltyBits += PatternPenalty(trimmed);
            penaltyBits += YearPenalty(trimmed);
            penaltyBits += HomogeneousClassPenalty(compositionFlags, trimmed);

            double entropy = Math.Max(0, baseEntropy - penaltyBits);
            double rawScore = entropy / MaxEntropyBits * 100.0;
            double score = Math.Max(0, Math.Min(100, rawScore));

            if (
                score < 100
                && compositionFlags.CategoryCount >= 4
                && trimmed.Length >= 16
                && entropy >= 90
            )
            {
                score = Math.Min(100, score + 5);
            }

            PasswordStrength strength = GetStrengthFromScore(score);
            string description = BuildDescription(strength, entropy, compositionFlags, trimmed);

            return new PasswordStrengthAnalysis(strength, description, Math.Round(score, 2));
        }

        public string GeneratePassword(int length, PasswordGenerationOptions options)
        {
            if (length <= 0)
            {
                throw new ValidationException(
                    ValidationErrorCode.PasswordLengthNonPositive,
                    Messages.PasswordLengthNonPositive,
                    nameof(length)
                );
            }

            if (options == PasswordGenerationOptions.None)
            {
                throw new ValidationException(
                    ValidationErrorCode.PasswordOptionsNone,
                    Messages.PasswordOptionsNone,
                    nameof(options)
                );
            }

            StringBuilder charSet = new();

            if (options.HasFlag(PasswordGenerationOptions.IncludeUppercase))
            {
                charSet.Append(UppercaseChars);
            }

            if (options.HasFlag(PasswordGenerationOptions.IncludeLowercase))
            {
                charSet.Append(LowercaseChars);
            }

            if (options.HasFlag(PasswordGenerationOptions.IncludeNumbers))
            {
                charSet.Append(NumberChars);
            }

            if (options.HasFlag(PasswordGenerationOptions.IncludeSpecialCharacters))
            {
                charSet.Append(SpecialChars);
            }

            string availableChars = charSet.ToString();

            if (options.HasFlag(PasswordGenerationOptions.ExcludeSimilarCharacters))
            {
                availableChars = new string(
                    availableChars.Where(c => !SimilarChars.Contains(c)).ToArray()
                );
            }

            if (string.IsNullOrEmpty(availableChars))
            {
                throw new ValidationException(
                    ValidationErrorCode.NoCharactersAvailableForGeneration,
                    Messages.NoCharactersAvailable
                );
            }

            StringBuilder password = new(length);
            using RandomNumberGenerator rng = RandomNumberGenerator.Create();

            byte[] buffer = new byte[length];
            rng.GetBytes(buffer);

            for (int i = 0; i < length; i++)
            {
                password.Append(availableChars[buffer[i] % availableChars.Length]);
            }

            return password.ToString();
        }

        private static int EstimatePoolSize(string password, out PasswordComposition flags)
        {
            bool hasUpper = UpperCaseRegex.IsMatch(password);
            bool hasLower = LowerCaseRegex.IsMatch(password);
            bool hasDigit = NumberRegex.IsMatch(password);
            bool hasSpecial = SpecialCharRegex.IsMatch(password);
            bool hasOther = password.Any(c => c > 127);

            int size = 0;

            if (hasLower)
            {
                size += 26;
            }

            if (hasUpper)
            {
                size += 26;
            }

            if (hasDigit)
            {
                size += 10;
            }

            if (hasSpecial)
            {
                size += 32;
            }

            if (hasOther)
            {
                size += 50;
            }

            flags = new PasswordComposition(hasUpper, hasLower, hasDigit, hasSpecial, hasOther);

            return size;
        }

        private static double RepetitionPenalty(string password)
        {
            double penalty = 0;
            int runLength = 1;

            for (int i = 1; i < password.Length; i++)
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

        private static double SequencePenalty(string password)
        {
            double penalty = 0;
            string lower = password.ToLowerInvariant();

            foreach (string seq in LinearSequences)
            {
                penalty += SequenceScan(lower, seq);
                string rev = new(seq.Reverse().ToArray());
                penalty += SequenceScan(lower, rev);
            }

            return penalty;
        }

        private static double SequenceScan(string passwordLower, string sequence)
        {
            double penalty = 0;

            for (int i = 0; i <= passwordLower.Length - 3; i++)
            {
                int max = Math.Min(sequence.Length, passwordLower.Length - i);
                int len = 0;

                for (int j = 0; j < max; j++)
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

        private static double PatternPenalty(string password)
        {
            double penalty = 0;
            string lower = password.ToLowerInvariant();

            penalty = CommonSubstrings.Where(lower.Contains).Sum(p => 6);
            string canon = NormalizeLeet(lower);
            penalty += CommonSubstrings.Where(canon.Contains).Sum(p => 6);

            return penalty;
        }

        private static double YearPenalty(string password)
        {
            return YearRegex.Matches(password).Count * 4.0;
        }

        private static double HomogeneousClassPenalty(PasswordComposition flags, string password)
        {
            return flags.CategoryCount <= 1 ? Math.Min(20, password.Length * 2)
                : flags.CategoryCount == 2 && password.Length < 10 ? 10
                : 0;
        }

        private static string NormalizeLeet(string input)
        {
            StringBuilder sb = new(input.Length);

            foreach (char c in input)
            {
                if (LeetMap.TryGetValue(c, out char mapped))
                {
                    sb.Append(mapped);
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        private static PasswordStrength GetStrengthFromScore(double score)
        {
            return score switch
            {
                >= 85 => PasswordStrength.Strong,
                >= 65 => PasswordStrength.Good,
                >= 45 => PasswordStrength.Fair,
                >= 25 => PasswordStrength.Weak,
                _ => PasswordStrength.VeryWeak,
            };
        }

        private static string BuildDescription(
            PasswordStrength strength,
            double entropy,
            PasswordComposition flags,
            string password
        )
        {
            StringBuilder sb = new();
            sb.Append(
                strength switch
                {
                    PasswordStrength.VeryWeak => Messages.StrengthVeryWeak,
                    PasswordStrength.Weak => Messages.StrengthWeak,
                    PasswordStrength.Fair => Messages.StrengthFair,
                    PasswordStrength.Good => Messages.StrengthGood,
                    PasswordStrength.Strong => Messages.StrengthStrong,
                    _ => "?",
                }
            );

            sb.Append($" // {string.Format(Messages.EntropyFormat, entropy.ToString("0.0"))}");

            List<string> tips = [];

            if (password.Length < 12)
            {
                tips.Add(Messages.TipIncreaseLength);
            }

            if (!flags.HasUpper)
            {
                tips.Add(Messages.TipAddUppercase);
            }

            if (!flags.HasLower)
            {
                tips.Add(Messages.TipAddLowercase);
            }

            if (!flags.HasDigit)
            {
                tips.Add(Messages.TipAddDigits);
            }

            if (!flags.HasSpecial)
            {
                tips.Add(Messages.TipAddSymbols);
            }

            if (flags.CategoryCount < 4 && password.Length < 16)
            {
                tips.Add(Messages.TipMoreVariety);
            }

            if (HasObviousSequence(password))
            {
                tips.Add(Messages.TipAvoidSequences);
            }

            if (HasRepeats(password))
            {
                tips.Add(Messages.TipReduceRepeats);
            }

            if (YearRegex.IsMatch(password))
            {
                tips.Add(Messages.TipAvoidYears);
            }

            if (tips.Count > 0)
            {
                sb.Append($" // {Messages.Suggestions} ");
                sb.Append(string.Join(", ", tips.Take(3)));
            }
            else if (strength == PasswordStrength.Strong)
            {
                sb.Append($" // {Messages.GoodJob}");
            }

            return sb.ToString();
        }

        private static bool HasObviousSequence(string password)
        {
            string lower = password.ToLowerInvariant();

            return LinearSequences.Any(seq => lower.Contains(seq[..Math.Min(seq.Length, 4)]))
                || LinearSequences.Any(seq =>
                {
                    string rev = new(seq.Reverse().ToArray());
                    return lower.Contains(rev[..Math.Min(rev.Length, 4)]);
                });
        }

        private static bool HasRepeats(string password)
        {
            for (int i = 1; i < password.Length; i++)
            {
                if (password[i] == password[i - 1])
                {
                    return true;
                }
            }

            return false;
        }
    }
}
