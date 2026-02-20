using CloudZCrypt.Domain.Strategies.Interfaces;
using CloudZCrypt.Terminal.Resources;
using Spectre.Console;

namespace CloudZCrypt.Terminal.Commands;

internal sealed class AlgorithmInfoCommand(
    IReadOnlyList<IEncryptionAlgorithmStrategy> encryptionStrategies,
    IReadOnlyList<IKeyDerivationAlgorithmStrategy> keyDerivationStrategies,
    IReadOnlyList<INameObfuscationStrategy> nameObfuscationStrategies,
    IReadOnlyList<ICompressionStrategy> compressionStrategies
)
{
    public void Execute()
    {
        AnsiConsole.Write(
            new Rule($"[bold cyan]{Messages.AlgorithmInformation}[/]").RuleStyle(
                Style.Parse("grey")
            )
        );
        AnsiConsole.WriteLine();

        PrintStrategyTable(
            Messages.EncryptionAlgorithms,
            encryptionStrategies,
            s => (s.DisplayName, s.Description)
        );
        PrintStrategyTable(
            Messages.KeyDerivationAlgorithms,
            keyDerivationStrategies,
            s => (s.DisplayName, s.Description)
        );
        PrintStrategyTable(
            Messages.NameObfuscationModes,
            nameObfuscationStrategies,
            s => (s.DisplayName, s.Description)
        );
        PrintStrategyTable(
            Messages.CompressionModes,
            compressionStrategies,
            s => (s.DisplayName, s.Description)
        );
    }

    private static void PrintStrategyTable<T>(
        string title,
        IReadOnlyList<T> strategies,
        Func<T, (string DisplayName, string Description)> selector
    )
    {
        Table table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey)
            .ShowRowSeparators()
            .Title($"[bold green]{title}[/]")
            .AddColumn(new TableColumn($"[bold]{Messages.Name}[/]").LeftAligned())
            .AddColumn(new TableColumn($"[bold]{Messages.Description}[/]").LeftAligned());

        foreach (T strategy in strategies)
        {
            (string displayName, string description) = selector(strategy);
            table.AddRow(Markup.Escape(displayName), Markup.Escape(description));
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }
}
