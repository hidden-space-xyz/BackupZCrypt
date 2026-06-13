using System.Globalization;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Domain.ValueObjects.Localization;

namespace BackupZCrypt.Desktop.Services;

// Resolves the language-neutral MessageCode/LocalizableMessage produced by the
// lower layers into a localized string. This is where backup/validation messages
// get their translation — the layers that create them never see localized text.
internal static class MessageLocalizer
{
    public static string Localize(LocalizableMessage message)
    {
        var format = Strings.GetByKey(message.Code.ToString());

        return message.Args.Count == 0
            ? format
            : string.Format(CultureInfo.CurrentUICulture, format, [.. message.Args]);
    }

    public static IEnumerable<string> Localize(IEnumerable<LocalizableMessage> messages)
    {
        return messages.Select(Localize);
    }
}
