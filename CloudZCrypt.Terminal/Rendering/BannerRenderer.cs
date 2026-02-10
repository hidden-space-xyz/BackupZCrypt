using Spectre.Console;

namespace CloudZCrypt.Terminal.Rendering;

internal static class BannerRenderer
{
    public static void Print()
    {
        AnsiConsole.Write(
            new FigletText("CloudZCrypt")
                .Color(Color.Cyan1)
                .Centered()
        );
        AnsiConsole.Write(
            new Rule("[dim]Secure File Encryption Tool[/]")
                .RuleStyle(Style.Parse("grey"))
                .Centered()
        );
        AnsiConsole.WriteLine();
    }
}
