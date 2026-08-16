namespace SharpTS.Compilation;

/// <summary>
/// The files a single compilation produces: the assembly image, plus debug symbols when
/// <see cref="ILCompiler.EmitDebugSymbols"/> is set.
/// </summary>
/// <param name="Assembly">The finished PE image, after any assembly-reference rewriting.</param>
/// <param name="Pdb">Serialized portable PDB, or null when symbols were not requested.</param>
/// <param name="PdbFileName">
/// File name recorded in the assembly's CodeView debug directory entry, or null when there is no
/// PDB. Callers writing to disk must use this name so debuggers can find the symbols.
/// </param>
/// <remarks>
/// Returned by <see cref="ILCompiler.SaveArtifacts"/>. <see cref="ILCompiler.SaveToBytes"/> remains
/// the assembly-only shorthand for in-memory and test callers.
/// </remarks>
public sealed record CompilationArtifacts(byte[] Assembly, byte[]? Pdb, string? PdbFileName);
