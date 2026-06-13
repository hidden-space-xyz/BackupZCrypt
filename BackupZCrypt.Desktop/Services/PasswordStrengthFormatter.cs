using System.Globalization;
using System.Text;
using BackupZCrypt.Application.ValueObjects.Password;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Desktop.Services;

/// <summary>
/// Builds the password-strength caption from the structured analysis the Application layer produces
/// (strength, entropy, tip codes). The string assembly and its translation live here, in the presentation layer.
/// </summary>
internal static class PasswordStrengthFormatter
{
    /// <summary>
    /// Formats a password-strength analysis into a single localized caption combining the strength label,
    /// entropy and up to three improvement suggestions.
    /// </summary>
    /// <param name="analysis">The structured strength analysis to format.</param>
    /// <returns>The localized caption.</returns>
    public static string Format(PasswordStrengthAnalysis analysis)
    {
        var label = analysis.Strength switch
        {
            PasswordStrength.VeryWeak => MessageCode.StrengthVeryWeak,
            PasswordStrength.Weak => MessageCode.StrengthWeak,
            PasswordStrength.Fair => MessageCode.StrengthFair,
            PasswordStrength.Good => MessageCode.StrengthGood,
            PasswordStrength.Strong => MessageCode.StrengthStrong,
            _ => MessageCode.StrengthVeryWeak,
        };

        StringBuilder sb = new();
        sb.Append(MessageLocalizer.Localize(new LocalizableMessage(label)))
            .Append(" // ")
            .Append(
            MessageLocalizer.Localize(
                new LocalizableMessage(
                    MessageCode.EntropyFormat,
                    analysis.Entropy.ToString("0.0", CultureInfo.CurrentCulture)
                )
            )
        );

        if (analysis.Tips.Count > 0)
        {
            sb.Append(" // ")
                .Append(MessageLocalizer.Localize(new LocalizableMessage(MessageCode.Suggestions)))
                .Append(' ')
                .AppendJoin(
                ", ",
                analysis
                    .Tips.Take(3)
                    .Select(static tip => MessageLocalizer.Localize(new LocalizableMessage(tip)))
            );
        }
        else if (analysis.Strength == PasswordStrength.Strong)
        {
            sb.Append(" // ")
                .Append(MessageLocalizer.Localize(new LocalizableMessage(MessageCode.GoodJob)));
        }

        return sb.ToString();
    }
}
