using System.Globalization;

using BackupZCrypt.Application.Common;
using BackupZCrypt.Desktop.Resources;

namespace BackupZCrypt.Desktop.Presentation;

/// <summary>
/// Resolves the language-neutral <see cref="LocalizableMessage"/> values produced by the lower layers
/// into localized strings. This is where backup and validation messages are translated; the layers that
/// create them never see localized text.
/// </summary>
/// <remarks>
/// Every <see cref="MessageCode"/> member resolves to a resource key of exactly the same name, and codes whose
/// name ends in <c>Format</c> take the message's arguments as <c>string.Format</c> placeholders. An argument
/// that is itself a <see cref="LocalizableMessage"/>, such as the reason a file failed, is localized first, so
/// the whole sentence follows the user's language. A <see cref="ByteCount"/> is written with a size unit and a
/// <see cref="PathSample"/> as a list, decided here rather than by the layer that raised the message.
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
        ArgumentNullException.ThrowIfNull(message);

        var format = Strings.GetByKey(message.Code.ToString());

        return message.Args.Count is 0
            ? format
            : string.Format(
                CultureInfo.CurrentCulture,
                format,
                [.. message.Args.Select(LocalizeArgument)]
            );
    }

    /// <summary>
    /// Localizes one format argument: a nested message is localized, an amount of storage is written
    /// with a size unit, a sample of paths is listed, and anything else is left for the format string
    /// to render.
    /// </summary>
    /// <param name="argument">The argument as the lower layers produced it.</param>
    /// <returns>The argument ready to be formatted into the message.</returns>
    private static object LocalizeArgument(object argument)
    {
        return argument switch
        {
            LocalizableMessage nested => Localize(nested),
            ByteCount size => ByteSizeFormatter.Format(size.Bytes),
            PathSample sample => ListPaths(sample),
            _ => argument,
        };
    }

    /// <summary>
    /// Lists the paths of a sample with the localized separator, closing the list with the localized
    /// entry for the paths left out when there were more.
    /// </summary>
    /// <param name="sample">The sample to list.</param>
    /// <returns>The listed paths.</returns>
    private static string ListPaths(PathSample sample)
    {
        IEnumerable<string> entries = sample.IsTruncated
            ? [.. sample.Paths, Strings.ListMore]
            : sample.Paths;

        return string.Join(Strings.ListSeparator, entries);
    }
}
