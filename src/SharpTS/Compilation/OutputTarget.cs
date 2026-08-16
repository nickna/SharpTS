namespace SharpTS.Compilation;

/// <summary>
/// Specifies the output type for compiled assemblies.
/// </summary>
public enum OutputTarget
{
    /// <summary>Class library (DLL) - default output type.</summary>
    Dll,
    /// <summary>Executable (EXE) - console application with entry point.</summary>
    Exe
}
