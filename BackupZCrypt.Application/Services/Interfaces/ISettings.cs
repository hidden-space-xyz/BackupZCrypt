namespace BackupZCrypt.Application.Services.Interfaces;

public interface ISettings<TSelf>
    where TSelf : class, ISettings<TSelf>
{
    static abstract TSelf DefaultValue { get; }

    static abstract string FileName { get; }
}
