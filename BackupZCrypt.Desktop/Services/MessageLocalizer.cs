using System.Globalization;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Desktop.Services;

/// <summary>
/// Resolves the language-neutral <see cref="LocalizableMessage"/> values produced by the lower layers
/// into localized strings. This is where backup and validation messages are translated; the layers that
/// create them never see localized text.
/// </summary>
internal static class MessageLocalizer
{
    /// <summary>
    /// Localizes a single message, resolving its code against the resources and applying any format arguments.
    /// </summary>
    /// <param name="message">The language-neutral message to localize.</param>
    /// <returns>The localized, formatted string.</returns>
    public static string Localize(LocalizableMessage message)
    {
        var format = Strings.GetByKey(message.Code.ToString());

        return message.Args.Count == 0
            ? format
            : string.Format(CultureInfo.CurrentUICulture, format, [.. message.Args]);
    }

    /// <summary>
    /// Localizes a sequence of messages.
    /// </summary>
    /// <param name="messages">The language-neutral messages to localize.</param>
    /// <returns>The localized strings, one per input message.</returns>
    public static IEnumerable<string> Localize(IEnumerable<LocalizableMessage> messages)
    {
        return messages.Select(Localize);
    }
}
