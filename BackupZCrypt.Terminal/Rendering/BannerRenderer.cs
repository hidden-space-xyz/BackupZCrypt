using BackupZCrypt.Terminal.Resources;
using Spectre.Console;

namespace BackupZCrypt.Terminal.Rendering;

internal static class BannerRenderer
{
    public static void Print()
    {
        AnsiConsole.Write(new FigletText("BackupZCrypt").Color(Color.Cyan1).Centered());
        AnsiConsole.Write(
            new Rule($"[dim]{Messages.BannerSubtitle}[/]").RuleStyle(Style.Parse("cyan")).Centered()
        );
        AnsiConsole.WriteLine();
    }
}
