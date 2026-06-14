using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using BackupZCrypt.Desktop.Models;
using BackupZCrypt.Desktop.Resources;
using BackupZCrypt.Desktop.Services;
using BackupZCrypt.Domain.Strategies.Interfaces;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// ViewModel for the about page: lists the available algorithms with their descriptions and the
/// application version.
/// </summary>
public sealed class AboutViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AboutViewModel"/> class, building the algorithm
    /// description lists from the registered strategies and resolving the assembly version.
    /// </summary>
    /// <param name="encryptionStrategies">The available encryption algorithm strategies.</param>
    /// <param name="keyDerivationStrategies">The available key-derivation algorithm strategies.</param>
    /// <param name="compressionStrategies">The available compression strategies.</param>
    public AboutViewModel(
        IEnumerable<IEncryptionAlgorithmStrategy> encryptionStrategies,
        IEnumerable<IKeyDerivationAlgorithmStrategy> keyDerivationStrategies,
        IEnumerable<ICompressionStrategy> compressionStrategies
    )
    {
        EncryptionAlgorithms =
        [
            .. encryptionStrategies
                .OrderBy(static s => s.Id)
                .Select(static s => new AlgorithmInfo(
                    AlgorithmMetadataProvider.GetName(s.Id),
                    AlgorithmMetadataProvider.GetSummary(s.Id),
                    AlgorithmMetadataProvider.GetDescription(s.Id)
                )),
        ];

        KeyDerivationAlgorithms =
        [
            .. keyDerivationStrategies
                .OrderBy(static s => s.Id)
                .Select(static s => new AlgorithmInfo(
                    AlgorithmMetadataProvider.GetName(s.Id),
                    AlgorithmMetadataProvider.GetSummary(s.Id),
                    AlgorithmMetadataProvider.GetDescription(s.Id)
                )),
        ];

        CompressionAlgorithms =
        [
            .. compressionStrategies
                .OrderBy(static s => s.Id)
                .Select(static s => new AlgorithmInfo(
                    AlgorithmMetadataProvider.GetName(s.Id),
                    AlgorithmMetadataProvider.GetSummary(s.Id),
                    AlgorithmMetadataProvider.GetDescription(s.Id)
                )),
        ];

        var version =
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        VersionText = string.Format(CultureInfo.CurrentCulture, Strings.VersionFormat, version);
    }

    /// <summary>
    /// Gets the available encryption algorithms with their descriptions.
    /// </summary>
    public ObservableCollection<AlgorithmInfo> EncryptionAlgorithms { get; }

    /// <summary>
    /// Gets the available key-derivation algorithms with their descriptions.
    /// </summary>
    public ObservableCollection<AlgorithmInfo> KeyDerivationAlgorithms { get; }

    /// <summary>
    /// Gets the available compression algorithms with their descriptions.
    /// </summary>
    public ObservableCollection<AlgorithmInfo> CompressionAlgorithms { get; }

    /// <summary>
    /// Gets the formatted application version caption.
    /// </summary>
    public string VersionText { get; }
}
