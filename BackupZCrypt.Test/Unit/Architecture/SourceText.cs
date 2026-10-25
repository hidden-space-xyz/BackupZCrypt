namespace BackupZCrypt.Test.Unit.Architecture;

/// <summary>
/// Character tests shared by the architecture suites that scan source text for the names it mentions.
/// </summary>
internal static class SourceText
{
    /// <summary>
    /// Determines whether a character can be part of an identifier: a letter, a decimal digit, or an
    /// underscore.
    /// </summary>
    /// <param name="c">The character to test.</param>
    /// <returns><see langword="true"/> for a word character.</returns>
    public static bool IsWordCharacter(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    /// <summary>
    /// Determines whether a position of a text holds a word character, treating positions outside the
    /// text as holding none.
    /// </summary>
    /// <param name="text">The text to read.</param>
    /// <param name="index">The position to test, which may lie outside the text.</param>
    /// <returns><see langword="true"/> when the position holds a word character.</returns>
    public static bool IsWordCharacterAt(string text, int index)
    {
        return index >= 0 && index < text.Length && IsWordCharacter(text[index]);
    }

    /// <summary>
    /// Determines whether a position of a text holds the given character, treating positions outside
    /// the text as holding none.
    /// </summary>
    /// <param name="text">The text to read.</param>
    /// <param name="index">The position to test, which may lie outside the text.</param>
    /// <param name="c">The expected character.</param>
    /// <returns><see langword="true"/> when the position holds <paramref name="c"/>.</returns>
    public static bool IsAt(string text, int index, char c)
    {
        return index >= 0 && index < text.Length && text[index] == c;
    }

    /// <summary>
    /// Moves past the characters of a text that satisfy a condition.
    /// </summary>
    /// <param name="text">The text to read.</param>
    /// <param name="index">The position to start from.</param>
    /// <param name="condition">The condition each skipped character satisfies.</param>
    /// <returns>The first position at or after <paramref name="index"/> whose character fails the condition, or the end of the text.</returns>
    public static int SkipWhile(string text, int index, Func<char, bool> condition)
    {
        while (index < text.Length && condition(text[index]))
        {
            index++;
        }

        return index;
    }
}
