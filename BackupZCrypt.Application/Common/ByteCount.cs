namespace BackupZCrypt.Application.Common;

/// <summary>
/// An amount of storage carried as a message argument, so the presentation layer decides how to
/// write it, such as in kilobytes or megabytes.
/// </summary>
/// <param name="Bytes">The amount, in bytes.</param>
public readonly record struct ByteCount(long Bytes);
