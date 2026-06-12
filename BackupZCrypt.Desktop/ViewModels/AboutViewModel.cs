using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using BackupZCrypt.Desktop.Models;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Domain.Enums;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Desktop.ViewModels;

public sealed class AboutViewModel : ViewModelBase
{
    public AboutViewModel(
        IEnumerable<IEncryptionAlgorithmStrategy> encryptionStrategies,
        IEnumerable<IKeyDerivationAlgorithmStrategy> keyDerivationStrategies,
        IEnumerable<ICompressionStrategy> compressionStrategies
    )
    {
        EncryptionAlgorithms =
        [
            .. encryptionStrategies
                .Where(static s => s.Id != EncryptionAlgorithm.None)
                .OrderBy(static s => s.Id)
                .Select(static s => new AlgorithmInfo(s.DisplayName, s.Description)),
        ];

        KeyDerivationAlgorithms =
        [
            .. keyDerivationStrategies
                .OrderBy(static s => s.Id)
                .Select(static s => new AlgorithmInfo(s.DisplayName, s.Description)),
        ];

        CompressionAlgorithms =
        [
            .. compressionStrategies
                .OrderBy(static s => s.Id)
                .Select(static s => new AlgorithmInfo(s.DisplayName, s.Description)),
        ];

        var version =
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        VersionText = string.Format(CultureInfo.CurrentCulture, Strings.VersionFormat, version);
    }

    public ObservableCollection<AlgorithmInfo> EncryptionAlgorithms { get; }

    public ObservableCollection<AlgorithmInfo> KeyDerivationAlgorithms { get; }

    public ObservableCollection<AlgorithmInfo> CompressionAlgorithms { get; }

    public string VersionText { get; }
}
