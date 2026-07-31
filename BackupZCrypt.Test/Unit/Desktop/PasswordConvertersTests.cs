using System.Globalization;

using Avalonia.Data.Converters;

using BackupZCrypt.Desktop.Converters;

namespace BackupZCrypt.Test.Unit.Desktop;

/// <summary>
/// Unit tests for <see cref="PasswordConverters"/>. The masking character is not cosmetic: Avalonia disables
/// copy and cut while a text box is masked, so clearing it is what allows a password to be copied. An
/// inverted mapping would leave every password field both unmasked and copyable by default.
/// </summary>
public sealed class PasswordConvertersTests
{
    [Test]
    public void RevealToPasswordChar_MasksUnlessTheUserAskedToReveal()
    {
        var converter = (IValueConverter)PasswordConverters.RevealToPasswordChar;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                converter.Convert(false, typeof(char), null, CultureInfo.InvariantCulture),
                Is.EqualTo('●'),
                "A hidden password must be masked, which is also what keeps copy and cut disabled."
            );
            Assert.That(
                converter.Convert(true, typeof(char), null, CultureInfo.InvariantCulture),
                Is.EqualTo('\0'),
                "Only a deliberately revealed password may clear the mask and so become copyable."
            );
        }
    }
}
