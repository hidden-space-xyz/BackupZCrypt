using System.Globalization;
using System.Text;

using BackupZCrypt.Application.ValueObjects.Password;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Desktop.Services;

/// <summary>
/// Builds the password-strength caption from the structured analysis the Application layer produces
/// (strength, entropy, tip codes). The string assembly and its translation live here, in the presentation layer.
/// </summary>
/// <remarks>
/// Only the tips travel as <see cref="MessageCode"/> values, because only they are chosen by the
/// Application layer. The caption's own vocabulary — the strength labels, the entropy format, and the
/// two closing remarks — is picked here and so reads <see cref="Strings"/> directly rather than routing
/// presentation text through a language-neutral code the lower layers never emit.
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

        StringBuilder sb = new();
        _ = sb.Append(label)
            .Append(" // ")
            .AppendFormat(
            CultureInfo.CurrentUICulture,
            Strings.EntropyFormat,
            analysis.Entropy.ToString("0.0", CultureInfo.CurrentCulture)
        );

        if (analysis.Tips.Count > 0)
        {
            _ = sb.Append(" // ")
                .Append(Strings.Suggestions)
                .Append(' ')
                .AppendJoin(
                ", ",
                analysis
                    .Tips.Take(3)
                    .Select(static tip => MessageLocalizer.Localize(new LocalizableMessage(tip)))
            );
        }
        else if (analysis.Strength is PasswordStrength.Strong)
        {
            _ = sb.Append(" // ").Append(Strings.GoodJob);
        }

        return sb.ToString();
    }
}
