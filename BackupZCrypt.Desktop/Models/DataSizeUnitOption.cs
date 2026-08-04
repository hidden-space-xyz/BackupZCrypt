namespace BackupZCrypt.Desktop.Models;

/// <summary>
/// A selectable data-size unit used by the benchmark to convert a user-entered amount into bytes.
/// </summary>
/// <param name="Name">The unit symbol shown to the user (for example, <c>MB</c>, <c>GB</c>, <c>TB</c>).</param>
/// <param name="BytesPerUnit">The number of bytes in one unit, using binary (1024-based) multiples.</param>
internal sealed record class DataSizeUnitOption(string Name, long BytesPerUnit);
