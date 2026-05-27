using System.Text;
using BackupZCrypt.Application.Orchestrators.Interfaces;
using BackupZCrypt.Application.Services.Interfaces;
using BackupZCrypt.Application.ValueObjects.Backup;
using BackupZCrypt.Composition;
using BackupZCrypt.Domain.Strategies.Interfaces;
using BackupZCrypt.Terminal;
using BackupZCrypt.Terminal.Commands;
using BackupZCrypt.Terminal.Services;
using BackupZCrypt.Terminal.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

ServiceCollection services = [];
services.AddDomainServices();
services.AddApplicationServices();
var provider = services.BuildServiceProvider();

// Apply saved language preference before rendering any UI
var settingsService = provider.GetRequiredService<ISettingsService>();
var languageSettings = await settingsService.GetOrCreateAsync<LanguageSettings>();
SettingsCommand.ApplyLanguage(languageSettings.LanguageCode);

var orchestrator = provider.GetRequiredService<IBackupOrchestrator>();
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
List<ICompressionStrategy> compressionStrategies =
[
    .. provider.GetServices<ICompressionStrategy>().OrderBy(s => s.Id),
];

IPathPromptService pathPromptService = new PathPromptService(settingsService);

BackupCommand backupCommand = new(
    orchestrator,
    settingsService,
    passwordService,
    manifestService,
    pathPromptService,
    encryptionStrategies,
    keyDerivationStrategies,
    compressionStrategies
);

SettingsCommand settingsCommand = new(
    settingsService,
    encryptionStrategies,
    keyDerivationStrategies,
    compressionStrategies
);

AlgorithmInfoCommand algorithmInfoCommand = new(
    encryptionStrategies,
    keyDerivationStrategies,
    compressionStrategies
);

TerminalApplication app = new(backupCommand, settingsCommand, algorithmInfoCommand);

await app.RunAsync();
