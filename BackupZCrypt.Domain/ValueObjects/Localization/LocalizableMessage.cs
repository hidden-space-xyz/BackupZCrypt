namespace BackupZCrypt.Domain.ValueObjects.Localization;

/// <summary>
/// A user-facing message carried through the lower layers as a language-neutral
/// <see cref="MessageCode"/> plus its format arguments. The presentation layer resolves
/// the code to a localized string and applies the arguments; no translated text ever lives here.
/// </summary>
/// <param name="code">The language-neutral code identifying the message.</param>
/// <param name="args">The format arguments applied when the code is resolved to localized text.</param>
public sealed class LocalizableMessage(MessageCode code, params object[] args)
{
    /// <summary>
    /// Gets the language-neutral code identifying the message.
    /// </summary>
    public MessageCode Code { get; } = code;

    /// <summary>
    /// Gets the format arguments applied when the code is resolved to localized text.
    /// </summary>
    public IReadOnlyList<object> Args { get; } = args;
}
