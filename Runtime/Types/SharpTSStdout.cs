namespace SharpTS.Runtime.Types;

/// <summary>
/// Singleton marker for the process.stdout stream object.
/// </summary>
/// <remarks>
/// Provides synchronous writing to standard output and TTY detection.
/// The singleton pattern ensures only one stdout object exists, consistent with Node.js semantics.
/// </remarks>
public class SharpTSStdout
{
    public static readonly SharpTSStdout Instance = new();
    private SharpTSStdout() { }

    /// <summary>
    /// Writes data to standard output without a trailing newline.
    /// </summary>
    /// <param name="data">The string to write.</param>
    /// <returns>True on success.</returns>
    public bool Write(string data)
    {
        Console.Write(data);
        return true;
    }

    /// <summary>
    /// Returns true if stdout is connected to a terminal (not redirected).
    /// </summary>
    public bool IsTTY => !Console.IsOutputRedirected;

    public override string ToString() => "[object stdout]";
}
