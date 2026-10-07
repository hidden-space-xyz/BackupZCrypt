namespace BackupZCrypt.Application.Utilities.Helpers;

/// <summary>
/// Raised when a chunk file the manifest references does not exist in the backup.
/// </summary>
internal sealed class MissingChunkException : IOException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MissingChunkException"/> class.
    /// </summary>
    public MissingChunkException()
        : base("A chunk file referenced by the manifest is missing.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MissingChunkException"/> class with a message.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    public MissingChunkException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MissingChunkException"/> class with a message and
    /// the exception that caused it.
    /// </summary>
    /// <param name="message">The message describing the failure.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public MissingChunkException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
