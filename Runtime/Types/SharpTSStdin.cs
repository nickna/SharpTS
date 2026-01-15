namespace SharpTS.Runtime.Types;

/// <summary>
/// Singleton marker for the process.stdin stream object.
/// </summary>
/// <remarks>
/// Provides synchronous line reading from standard input and TTY detection.
/// The singleton pattern ensures only one stdin object exists, consistent with Node.js semantics.
/// </remarks>
public class SharpTSStdin
{
    public static readonly SharpTSStdin Instance = new();
    private SharpTSStdin() { }

    /// <summary>
    /// Reads a line from standard input.
    /// Returns null at end of file.
    /// </summary>
    public string? Read() => Console.ReadLine();

    /// <summary>
    /// Returns true if stdin is connected to a terminal (not redirected).
    /// </summary>
    public bool IsTTY => !Console.IsInputRedirected;

    public override string ToString() => "[object stdin]";
}
