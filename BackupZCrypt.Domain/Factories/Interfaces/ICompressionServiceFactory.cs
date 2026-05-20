using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Domain.Factories.Interfaces;

public interface ICompressionServiceFactory
{
    ICompressionStrategy Create(CompressionMode mode);
}
