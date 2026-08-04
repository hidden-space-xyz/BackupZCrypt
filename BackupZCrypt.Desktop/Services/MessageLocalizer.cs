using System.Globalization;

using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Desktop.Services;

/// <summary>
/// Resolves the language-neutral <see cref="LocalizableMessage"/> values produced by the lower layers
/// into localized strings. This is where backup and validation messages are translated; the layers that
/// create them never see localized text.
/// </summary>
/// <remarks>
/// Every <see cref="MessageCode"/> member resolves to a resource key of exactly the same name, and codes whose
/// name ends in <c>Format</c> take the message's arguments as <c>string.Format</c> placeholders.
/// </remarks>
internal static class MessageLocalizer
{
    /// <summary>
    /// Localizes a single message, resolving its code against the resources and applying any format arguments.
    /// </summary>
    /// <param name="message">The language-neutral message to localize.</param>
    /// <returns>The localized, formatted string, or the code's name when no resource matches it.</returns>
    public static string Localize(LocalizableMessage message)
    {
        var format = Strings.GetByKey(message.Code.ToString());

        return message.Args.Count is 0
            ? format
            : string.Format(CultureInfo.CurrentUICulture, format, [.. message.Args]);
    }
}
