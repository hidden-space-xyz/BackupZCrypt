using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Composition;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Terminal;
using BackupZCrypt.Terminal.Commands;
using BackupZCrypt.Terminal.Services;
using BackupZCrypt.Terminal.Services.Interfaces;

using Microsoft.Extensions.DependencyInjection;

using System.Text;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

ServiceCollection services = [];
services.AddDomainServices();
services.AddApplicationServices();
var provider = services.BuildServiceProvider();

var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();
var backupCreationSettingsService = provider.GetRequiredService<IBackupCreationSettingsService>();
var recentPathSettingsService = provider.GetRequiredService<IRecentPathSettingsService>();
var passwordService = provider.GetRequiredService<IPasswordService>();
var manifestService = provider.GetRequiredService<IManifestService>();
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

IPathPromptService pathPromptService = new PathPromptService(recentPathSettingsService);

BackupCommand backupCommand = new(
    orchestrator,
    backupCreationSettingsService,
    passwordService,
    manifestService,
    pathPromptService,
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
