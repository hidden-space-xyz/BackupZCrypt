using CloudZCrypt.Domain.Strategies.Interfaces;
using Spectre.Console;

namespace CloudZCrypt.Terminal.Commands;

internal sealed class AlgorithmInfoCommand(
    IReadOnlyList<IEncryptionAlgorithmStrategy> encryptionStrategies,
    IReadOnlyList<IKeyDerivationAlgorithmStrategy> keyDerivationStrategies,
    IReadOnlyList<INameObfuscationStrategy> nameObfuscationStrategies,
    IReadOnlyList<ICompressionStrategy> compressionStrategies)
{
    public void Execute()
    {
        AnsiConsole.Write(new Rule("[bold cyan]Algorithm Information[/]").RuleStyle(Style.Parse("grey")));
        AnsiConsole.WriteLine();

        PrintStrategyTable("Encryption Algorithms", encryptionStrategies, s => (s.DisplayName, s.Description));
        PrintStrategyTable("Key Derivation Algorithms", keyDerivationStrategies, s => (s.DisplayName, s.Description));
        PrintStrategyTable("Name Obfuscation Modes", nameObfuscationStrategies, s => (s.DisplayName, s.Description));
        PrintStrategyTable("Compression Modes", compressionStrategies, s => (s.DisplayName, s.Description));
    }

    private static void PrintStrategyTable<T>(
        string title,
        IReadOnlyList<T> strategies,
        Func<T, (string DisplayName, string Description)> selector)
    {
        Table table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .Title($"[bold green]{title}[/]")
            .AddColumn(new TableColumn("[bold]Name[/]").LeftAligned())
            .AddColumn(new TableColumn("[bold]Description[/]").LeftAligned());

        foreach (T strategy in strategies)
        {
            (string displayName, string description) = selector(strategy);
            table.AddRow(Markup.Escape(displayName), Markup.Escape(description));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }
}
