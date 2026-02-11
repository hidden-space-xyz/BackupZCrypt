using System.Text;
using CloudZCrypt.Application.Orchestrators.Interfaces;
using CloudZCrypt.Application.Services.Interfaces;
using CloudZCrypt.Composition;
using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Terminal;
using CloudZCrypt.Terminal.Commands;
using Microsoft.Extensions.DependencyInjection;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

ServiceCollection services = new();
services.AddDomainServices();
services.AddApplicationServices();
ServiceProvider provider = services.BuildServiceProvider();

IFileCryptOrchestrator orchestrator = provider.GetRequiredService<IFileCryptOrchestrator>();
IPasswordService passwordService = provider.GetRequiredService<IPasswordService>();
List<IEncryptionAlgorithmStrategy> encryptionStrategies =
[
    .. provider.GetServices<IEncryptionAlgorithmStrategy>().OrderBy(s => s.Id),
];
List<IKeyDerivationAlgorithmStrategy> keyDerivationStrategies =
[
    .. provider.GetServices<IKeyDerivationAlgorithmStrategy>().OrderBy(s => s.Id),
];
List<INameObfuscationStrategy> nameObfuscationStrategies =
[
    .. provider.GetServices<INameObfuscationStrategy>().OrderBy(s => s.Id),
];
List<ICompressionStrategy> compressionStrategies =
[
    .. provider.GetServices<ICompressionStrategy>().OrderBy(s => s.Id),
];

EncryptCommand encryptCommand = new(
    orchestrator,
    passwordService,
    encryptionStrategies,
    keyDerivationStrategies,
    nameObfuscationStrategies,
    compressionStrategies
);

GeneratePasswordCommand generatePasswordCommand = new(passwordService);

AlgorithmInfoCommand algorithmInfoCommand = new(
    encryptionStrategies,
    keyDerivationStrategies,
    nameObfuscationStrategies,
    compressionStrategies
);

TerminalApplication app = new(encryptCommand, generatePasswordCommand, algorithmInfoCommand);

await app.RunAsync();
