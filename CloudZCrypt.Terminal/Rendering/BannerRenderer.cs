namespace CloudZCrypt.Terminal.Rendering;

using CloudZCrypt.Terminal.Resources;
using Spectre.Console;

internal static class BannerRenderer
{
    public static void Print()
    {
        AnsiConsole.Write(new FigletText("CloudZCrypt").Color(Color.Cyan1).Centered());
        AnsiConsole.Write(
            new Rule($"[dim]{Messages.BannerSubtitle}[/]").RuleStyle(Style.Parse("grey")).Centered());
        AnsiConsole.WriteLine();
    }
}
