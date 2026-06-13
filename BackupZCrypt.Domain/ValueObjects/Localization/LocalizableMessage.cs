namespace BackupZCrypt.Domain.ValueObjects.Localization;

// A user-facing message carried through the lower layers as a language-neutral
// code plus its format arguments. The presentation layer resolves the code to a
// localized string and applies the arguments. No translated text ever lives here.
public sealed class LocalizableMessage
{
    public LocalizableMessage(MessageCode code, params object[] args)
    {
        this.Code = code;
        this.Args = args;
    }

    public MessageCode Code { get; }

    public IReadOnlyList<object> Args { get; }
}
