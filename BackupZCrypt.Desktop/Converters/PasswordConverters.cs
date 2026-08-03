using System.Globalization;

using Avalonia.Data.Converters;

using BackupZCrypt.Domain.Enums;

namespace BackupZCrypt.Desktop.Converters;

/// <summary>
/// Value converters used by password input fields.
/// </summary>
internal static class PasswordConverters
{
    /// <summary>
    /// The converter that maps the "reveal password" flag to the value bound to
    /// <c>TextBox.PasswordChar</c>: the default character (<c>'\0'</c>, no masking) when the password
    /// is revealed, or a bullet when it is hidden. Clearing the password character also re-enables
    /// copy and cut, which Avalonia disables while a field is masked — so the password can be copied
    /// only while it is visible.
    /// </summary>
    public static readonly FuncValueConverter<bool, char> RevealToPasswordChar = new(
        static reveal => reveal ? '\0' : '●'
    );

    /// <summary>
    /// The converter that reports whether a password strength falls in the colour band named by the
    /// converter parameter, so the strength bar is coloured by a style class rather than by a brush
    /// handed down from the ViewModel.
    /// </summary>
    /// <remarks>
    /// Mapping strength to a class here rather than to an <c>IBrush</c> in the ViewModel is what lets
    /// the colours stay <c>DynamicResource</c> lookups in <c>AppStyles.axaml</c>: they follow the
    /// active theme variant, which a brush resolved once in a ViewModel could not. It is bound
    /// through <c>Classes.name</c>, the one form Avalonia supports — <c>Classes</c> itself is a plain
    /// collection rather than an <c>AvaloniaProperty</c>, so it cannot be bound as a whole.
    /// </remarks>
    public static readonly IValueConverter StrengthIsBand = new StrengthBandConverter();

    /// <summary>
    /// Reports whether the bound <see cref="PasswordStrength"/> belongs to the band named by the
    /// converter parameter.
    /// </summary>
    private sealed class StrengthBandConverter : IValueConverter
    {
        /// <inheritdoc/>
        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        )
        {
            return value is PasswordStrength strength
                && parameter is string band
                && string.Equals(BandOf(strength), band, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        /// <exception cref="NotSupportedException">Always: a style class never writes back.</exception>
        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture
        ) => throw new NotSupportedException();

        /// <summary>
        /// Maps a <see cref="PasswordStrength"/> to the name of the colour band it belongs to.
        /// </summary>
        /// <param name="strength">The strength to classify.</param>
        /// <returns>The band name, matching a style class in <c>AppStyles.axaml</c>.</returns>
        private static string BandOf(PasswordStrength strength) =>
            strength switch
            {
                PasswordStrength.VeryWeak or PasswordStrength.Weak => "danger",
                PasswordStrength.Fair => "warning",
                PasswordStrength.Good => "good",
                _ => "strong",
            };
    }
}
