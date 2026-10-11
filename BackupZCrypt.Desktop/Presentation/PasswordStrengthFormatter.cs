using System.Globalization;

using BackupZCrypt.Application.Common;
using BackupZCrypt.Application.Passwords;
using BackupZCrypt.Desktop.Resources;

namespace BackupZCrypt.Desktop.Presentation;

/// <summary>
/// Builds the password-strength caption from the structured analysis the Application layer produces
/// (strength, entropy, tip codes). The string assembly and its translation live here, in the presentation layer.
/// </summary>
/// <remarks>
/// Only the tips travel as <see cref="MessageCode"/> values, because only they are chosen by the
/// Application layer. The caption's own vocabulary — the strength labels, the entropy format, the
/// two closing remarks, and the separators between its parts and between the suggestions — is picked
/// here and so reads <see cref="Strings"/> directly rather than routing presentation text through a
/// language-neutral code the lower layers never emit.
/// </remarks>
internal static class PasswordStrengthFormatter
{
    /// <summary>
    /// Formats a password-strength analysis into a single localized caption combining the strength label,
    /// entropy, and up to three improvement suggestions.
    /// </summary>
    /// <param name="analysis">The structured strength analysis to format.</param>
    /// <returns>The localized caption.</returns>
    public static string Format(PasswordStrengthAnalysis analysis)
    {
        var label = analysis.Strength switch
        {
            PasswordStrength.VeryWeak => Strings.StrengthVeryWeak,
            PasswordStrength.Weak => Strings.StrengthWeak,
            PasswordStrength.Fair => Strings.StrengthFair,
            PasswordStrength.Good => Strings.StrengthGood,
            PasswordStrength.Strong => Strings.StrengthStrong,
            _ => Strings.StrengthVeryWeak,
        };

        List<string> parts =
        [
            label,
            string.Format(
                CultureInfo.CurrentCulture,
                Strings.EntropyFormat,
                analysis.Entropy.ToString("0.0", CultureInfo.CurrentCulture)
            ),
        ];

        if (analysis.Tips.Count > 0)
        {
            parts.Add(
                string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.SuggestionsFormat,
                    string.Join(
                        Strings.ListSeparator,
                        analysis
                            .Tips.Take(3)
                            .Select(static tip => MessageLocalizer.Localize(new LocalizableMessage(tip)))
                    )
                )
            );
        }
        else if (analysis.Strength is PasswordStrength.Strong)
        {
            parts.Add(Strings.GoodJob);
        }

        return string.Join(Strings.CaptionSeparator, parts);
    }
}
