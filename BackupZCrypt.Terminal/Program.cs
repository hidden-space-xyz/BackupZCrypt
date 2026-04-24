using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Composition;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Terminal;
using BackupZCrypt.Terminal.Commands;

using Microsoft.Extensions.DependencyInjection;

using System.Text;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

ServiceCollection services = [];
services.AddDomainServices();
services.AddApplicationServices();
ServiceProvider provider = services.BuildServiceProvider();

IBackupOrchestrator orchestrator = provider.GetRequiredService<IBackupOrchestrator>();
IBackupCreationSettingsService backupCreationSettingsService = provider.GetRequiredService<IBackupCreationSettingsService>();
IPasswordService passwordService = provider.GetRequiredService<IPasswordService>();
IManifestService manifestService = provider.GetRequiredService<IManifestService>();
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
    backupCreationSettingsService,
    passwordService,
    manifestService,
    encryptionStrategies,
    keyDerivationStrategies,
    nameObfuscationStrategies,
    compressionStrategies);

BackupSettingsCommand backupSettingsCommand = new(
    backupCreationSettingsService,
    encryptionStrategies,
    keyDerivationStrategies,
    nameObfuscationStrategies,
    compressionStrategies);

AlgorithmInfoCommand algorithmInfoCommand = new(
    encryptionStrategies,
    keyDerivationStrategies,
    nameObfuscationStrategies,
    compressionStrategies);

TerminalApplication app = new(backupCommand, backupSettingsCommand, algorithmInfoCommand);

await app.RunAsync();
