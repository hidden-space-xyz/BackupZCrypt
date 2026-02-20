namespace CloudZCrypt.Domain.Factories.Interfaces;

using CloudZCrypt.Domain.Enums;
using CloudZCrypt.Domain.Strategies.Interfaces;

public interface INameObfuscationServiceFactory
{
    INameObfuscationStrategy Create(NameObfuscationMode obfuscationMode);
}
