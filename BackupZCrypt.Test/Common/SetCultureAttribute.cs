using System.Globalization;
using System.Reflection;

using Xunit.v3;

namespace BackupZCrypt.Test.Common;

/// <summary>
/// Pins the ambient culture for the duration of a single test, then restores whatever was in place
/// before it ran.
/// </summary>
/// <remarks>
/// xUnit has no built-in culture attribute, so this fills the gap NUnit's <c>[SetCulture]</c> left.
/// Scoping the culture to the test — rather than assigning <see cref="CultureInfo.CurrentCulture"/>
/// inside the test body — is what keeps a fixture that asserts on formatted text free of shared
/// mutable state: a test that fails part way through still hands the original culture back.
/// </remarks>
/// <param name="culture">
/// An RFC 5646 culture name; the empty string selects the invariant culture.
/// </param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
internal sealed class SetCultureAttribute(string culture) : BeforeAfterTestAttribute
{
    /// <summary>
    /// The culture to install, resolved once at construction so a malformed name fails at
    /// discovery rather than in the middle of a run.
    /// </summary>
    private readonly CultureInfo target = CultureInfo.GetCultureInfo(culture);

    /// <summary>
    /// The formatting culture that was current before the test, captured so it can be put back.
    /// </summary>
    private CultureInfo? previousCulture;

    /// <summary>
    /// The resource-lookup culture that was current before the test, captured so it can be put back.
    /// </summary>
    private CultureInfo? previousUiCulture;

    /// <inheritdoc/>
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        this.previousCulture = CultureInfo.CurrentCulture;
        this.previousUiCulture = CultureInfo.CurrentUICulture;

        CultureInfo.CurrentCulture = this.target;
        CultureInfo.CurrentUICulture = this.target;
    }

    /// <inheritdoc/>
    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        // Both fields are assigned by Before, which xUnit always runs first; the null-forgiving
        // operator states that rather than inventing a fallback culture that could never apply.
        CultureInfo.CurrentCulture = this.previousCulture!;
        CultureInfo.CurrentUICulture = this.previousUiCulture!;
    }
}
