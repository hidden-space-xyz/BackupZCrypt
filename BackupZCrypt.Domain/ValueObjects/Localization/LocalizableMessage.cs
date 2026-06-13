namespace BackupZCrypt.Domain.ValueObjects.Localization;

/// <summary>
/// A user-facing message carried through the lower layers as a language-neutral
/// <see cref="MessageCode"/> plus its format arguments. The presentation layer resolves
/// the code to a localized string and applies the arguments; no translated text ever lives here.
/// </summary>
public sealed class LocalizableMessage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizableMessage"/> class.
    /// </summary>
    /// <param name="code">The language-neutral code identifying the message.</param>
    /// <param name="args">The format arguments applied when the code is resolved to localized text.</param>
    public LocalizableMessage(MessageCode code, params object[] args)
    {
        this.Code = code;
        this.Args = args;
    }

    /// <summary>
    /// Gets the language-neutral code identifying the message.
    /// </summary>
    public MessageCode Code { get; }

    /// <summary>
    /// Gets the format arguments applied when the code is resolved to localized text.
    /// </summary>
    public IReadOnlyList<object> Args { get; }
}
