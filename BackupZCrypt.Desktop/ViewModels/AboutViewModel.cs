using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;

using BackupZCrypt.Application.Algorithms;
using BackupZCrypt.Desktop.Models;
using BackupZCrypt.Desktop.Presentation;
using BackupZCrypt.Desktop.Resources;

namespace BackupZCrypt.Desktop.ViewModels;

/// <summary>
/// ViewModel for the about page: lists the available algorithms with their descriptions and the
/// application version.
/// </summary>
internal sealed class AboutViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AboutViewModel"/> class, building the algorithm
    /// description lists from the algorithm catalog and resolving the assembly version.
    /// </summary>
    /// <param name="algorithmCatalog">The catalog of algorithms a new archive can be written with.</param>
    public AboutViewModel(IAlgorithmCatalog algorithmCatalog)
    {
        ArgumentNullException.ThrowIfNull(algorithmCatalog);

        EncryptionAlgorithms =
        [
            .. algorithmCatalog.EncryptionAlgorithms.Select(static id => new AlgorithmInfo(
                AlgorithmMetadataProvider.GetName(id),
                AlgorithmMetadataProvider.GetSummary(id),
                AlgorithmMetadataProvider.GetDescription(id)
            )),
        ];

        KeyDerivationAlgorithms =
        [
            .. algorithmCatalog.KeyDerivationAlgorithms.Select(static id => new AlgorithmInfo(
                AlgorithmMetadataProvider.GetName(id),
                AlgorithmMetadataProvider.GetSummary(id),
                AlgorithmMetadataProvider.GetDescription(id)
            )),
        ];

        CompressionAlgorithms =
        [
            .. algorithmCatalog.CompressionModes.Select(static id => new AlgorithmInfo(
                AlgorithmMetadataProvider.GetName(id),
                AlgorithmMetadataProvider.GetSummary(id),
                AlgorithmMetadataProvider.GetDescription(id)
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
    /// Gets the available compression modes with their descriptions, led by the "no compression" option
    /// so the list matches the choices offered on the settings page.
    /// </summary>
    public ObservableCollection<AlgorithmInfo> CompressionAlgorithms { get; }

    /// <summary>
    /// Gets the formatted application version caption.
    /// </summary>
    public string VersionText { get; }
}
