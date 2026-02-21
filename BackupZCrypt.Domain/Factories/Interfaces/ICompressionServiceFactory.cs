namespace BackupZCrypt.Domain.Factories.Interfaces;

using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

public interface ICompressionServiceFactory
{
    ICompressionStrategy Create(CompressionMode mode);
}
