// Fails when back-end (core assemblies) or front-end (Desktop) coverage drops below the minimum percentage.
// Usage: dotnet run --file scripts/check-coverage.cs -- <ReportGenerator Summary.json> [minimum, default 95]
using System.Globalization;
using System.Text.Json;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine(
        "Usage: dotnet run --file scripts/check-coverage.cs -- <Summary.json> [minimum]"
    );
    return 2;
}

var minimum = args.Length == 2 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 95;

string[] backEnd =
[
    "BackupZCrypt.Application",
    "BackupZCrypt.Composition",
    "BackupZCrypt.Domain",
    "BackupZCrypt.Infrastructure",
];
string[] frontEnd = ["BackupZCrypt"];

using var document = JsonDocument.Parse(File.ReadAllText(args[0]));
var assemblies = document
    .RootElement.GetProperty("coverage")
    .GetProperty("assemblies")
    .EnumerateArray()
    .ToDictionary(static a => a.GetProperty("name").GetString()!, Measure, StringComparer.Ordinal);

foreach (var (name, metrics) in assemblies.OrderBy(static a => a.Key, StringComparer.Ordinal))
{
    Print(name, metrics, enforced: false);
}

var failed = false;
failed |= !Print("Back end", Sum(backEnd), enforced: true);
failed |= !Print("Front end", Sum(frontEnd), enforced: true);

if (failed)
{
    Console.Error.WriteLine(
        string.Create(CultureInfo.InvariantCulture, $"Coverage is below the {minimum}% minimum.")
    );
    return 1;
}

return 0;

Metric[] Sum(string[] names)
{
    var missing = names.Where(name => !assemblies.ContainsKey(name)).ToArray();
    if (missing.Length > 0)
    {
        throw new InvalidOperationException("No coverage was collected for " + string.Join(", ", missing) + ".");
    }

    return
    [
        .. Enumerable
            .Range(0, 3)
            .Select(i => new Metric(
                assemblies[names[0]][i].Name,
                names.Sum(name => assemblies[name][i].Covered),
                names.Sum(name => assemblies[name][i].Total)
            )),
    ];
}

bool Print(string scope, Metric[] metrics, bool enforced)
{
    var passed = true;
    Console.WriteLine(enforced ? $"{scope} (minimum {minimum.ToString(CultureInfo.InvariantCulture)}%)" : scope);

    foreach (var (name, covered, total) in metrics)
    {
        var percentage = total == 0 ? 100 : covered * 100.0 / total;
        var ok = percentage >= minimum;
        passed &= ok;
        var verdict = enforced ? (ok ? " OK" : $" below {minimum}%") : string.Empty;
        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"  {name, -9} {percentage, 6:F2}% ({covered}/{total}){verdict}"
            )
        );
    }

    return passed;
}

static Metric[] Measure(JsonElement assembly)
{
    return
    [
        new("Lines", Count(assembly, "coveredlines"), Count(assembly, "coverablelines")),
        new("Branches", Count(assembly, "coveredbranches"), Count(assembly, "totalbranches")),
        new("Methods", Count(assembly, "coveredmethods"), Count(assembly, "totalmethods")),
    ];
}

static int Count(JsonElement element, string property)
{
    return element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.Number
        ? value.GetInt32()
        : 0;
}

internal sealed record Metric(string Name, int Covered, int Total);
