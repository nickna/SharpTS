namespace SharpTS.Runtime.Types;

/// <summary>
/// Singleton marker for the process.stderr stream object.
/// </summary>
/// <remarks>
/// Provides synchronous writing to standard error and TTY detection.
/// The singleton pattern ensures only one stderr object exists, consistent with Node.js semantics.
/// </remarks>
public class SharpTSStderr
{
    public static readonly SharpTSStderr Instance = new();
    private SharpTSStderr() { }

    /// <summary>
    /// Writes data to standard error without a trailing newline.
    /// </summary>
    /// <param name="data">The string to write.</param>
    /// <returns>True on success.</returns>
    public bool Write(string data)
    {
        Console.Error.Write(data);
        return true;
    }

    /// <summary>
    /// Returns true if stderr is connected to a terminal (not redirected).
    /// </summary>
    public bool IsTTY => !Console.IsErrorRedirected;

    public override string ToString() => "[object stderr]";
}
