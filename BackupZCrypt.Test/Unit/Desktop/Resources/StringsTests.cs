using System.Globalization;
using System.Reflection;

using BackupZCrypt.Desktop.Resources;

namespace BackupZCrypt.Test.Unit.Desktop.Resources;

/// <summary>
/// Unit tests for <see cref="Strings"/>: every accessor is named after a resource that exists in each shipped
/// language, so no label falls back to showing its own key.
/// </summary>
public sealed class StringsTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    internal void EveryAccessor_ResolvesATranslatedResource(string culture)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);

        try
        {
            var missing = typeof(Strings)
                .GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(static p => p.PropertyType == typeof(string))
                .Where(static p => p.GetValue(null) is not string value || value.Length == 0 || value == p.Name)
                .Select(static p => p.Name)
                .ToArray();

            Assert.Empty(missing);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    internal void GetByKey_UnknownKey_FallsBackToTheKey()
    {
        Assert.Equal("NoSuchResourceKey", Strings.GetByKey("NoSuchResourceKey"));
    }
}
