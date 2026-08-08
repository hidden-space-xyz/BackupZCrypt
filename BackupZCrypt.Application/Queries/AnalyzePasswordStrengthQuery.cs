using System.Diagnostics.CodeAnalysis;
using System.Text;

using BackupZCrypt.Application.Queries.Interfaces;
using BackupZCrypt.Application.ValueObjects.Password;

namespace BackupZCrypt.Application.Queries;

/// <summary>
/// Requests a strength analysis of a candidate password. Answered synchronously because the UI asks
/// on every keystroke.
/// </summary>
/// <param name="Password">The password to analyze.</param>
public sealed record class AnalyzePasswordStrengthQuery(string Password)
    : IQuery<PasswordStrengthAnalysis>
{
    /// <summary>
    /// Writes the record's members for <see cref="object.ToString"/>, substituting a placeholder for
    /// <see cref="Password"/>.
    /// </summary>
    /// <remarks>
    /// A record prints every member by default, so the compiler-generated implementation would put
    /// the plaintext password into any log line, exception message, or debugger watch that formats
    /// the query. Redacting it costs nothing and closes the whole class of accident.
    /// </remarks>
    /// <param name="builder">The builder receiving the formatted members.</param>
    /// <returns><see langword="true"/>, since at least one member was written.</returns>
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The record contract requires PrintMembers to be an instance method, and the "
            + "body touches no instance state only because the type's single member is the secret "
            + "being redacted."
    )]
    private bool PrintMembers(StringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _ = builder.Append("Password = ***");

        return true;
    }
}
