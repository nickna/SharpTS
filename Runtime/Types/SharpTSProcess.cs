namespace SharpTS.Runtime.Types;

/// <summary>
/// Singleton marker for the JavaScript process object.
/// </summary>
/// <remarks>
/// This class exists primarily as a type marker for <c>process.xxx</c> access resolution.
/// The actual process properties (env, argv, platform, etc.) and methods (cwd(), exit())
/// are handled as special cases in the interpreter and compiler. The singleton pattern
/// ensures only one process object exists, consistent with Node.js semantics.
/// </remarks>
public class SharpTSProcess
{
    public static readonly SharpTSProcess Instance = new();
    private SharpTSProcess() { }

    public override string ToString() => "[object process]";
}
