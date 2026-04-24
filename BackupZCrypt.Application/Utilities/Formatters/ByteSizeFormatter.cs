namespace BackupZCrypt.Application.Utilities.Formatters;

public static class ByteSizeFormatter
{
    private static readonly string[] Suffixes = ["B", "KB", "MB", "GB", "TB"];

    public static string Format(long bytes)
    {
        if (bytes == 0)
        {
            return "0 B";
        }

        double size = Math.Abs(bytes);
        var suffixIndex = 0;
        while (size >= 1024 && suffixIndex < Suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:F1} {Suffixes[suffixIndex]}";
    }
}
