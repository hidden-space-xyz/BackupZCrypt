using System.Text;
using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Composition;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Terminal;
using BackupZCrypt.Terminal.Commands;
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

BackupCommand backupCommand = new(
    orchestrator,
    passwordService,
    encryptionStrategies,
    keyDerivationStrategies,
    nameObfuscationStrategies,
    compressionStrategies);

AlgorithmInfoCommand algorithmInfoCommand = new(
    encryptionStrategies,
    keyDerivationStrategies,
    nameObfuscationStrategies,
    compressionStrategies);

TerminalApplication app = new(backupCommand, algorithmInfoCommand);

await app.RunAsync();
