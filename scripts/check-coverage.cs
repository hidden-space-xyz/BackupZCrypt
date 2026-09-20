// Fails when core coverage drops below the minimum percentage.
// Usage: dotnet run --file scripts/check-coverage.cs -- <ReportGenerator Summary.json> [minimum, default 90]
using System.Globalization;
using System.Text.Json;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine(
        "Usage: dotnet run --file scripts/check-coverage.cs -- <Summary.json> [minimum]"
    );
    return 2;
}

var minimum = args.Length == 2 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 90;

using var document = JsonDocument.Parse(File.ReadAllText(args[0]));
var summary = document.RootElement.GetProperty("summary");

(string Name, int Covered, int Total)[] metrics =
[
    ("Lines", Count("coveredlines"), Count("coverablelines")),
    ("Branches", Count("coveredbranches"), Count("totalbranches")),
    ("Methods", Count("coveredmethods"), Count("totalmethods")),
];

var failed = false;
foreach (var (name, covered, total) in metrics)
{
    var percentage = total == 0 ? 100 : covered * 100.0 / total;
    var passed = percentage >= minimum;
    failed |= !passed;
    Console.WriteLine(
        string.Create(
            CultureInfo.InvariantCulture,
            $"{name, -9} {percentage, 6:F2}% ({covered}/{total}) {(passed ? "OK" : $"below {minimum}%")}"
        )
    );
}

if (failed)
{
    Console.Error.WriteLine(
        string.Create(
            CultureInfo.InvariantCulture,
            $"Core coverage is below the {minimum}% minimum."
        )
    );
    return 1;
}

return 0;

int Count(string property) => summary.GetProperty(property).GetInt32();
