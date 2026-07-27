using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the util-inspect helper bodies into $Runtime. Signatures are defined
/// early by DefineUtilInspectSignatures (RuntimeClass) so ConsoleDir can
/// reference them; bodies are emitted here (they call each other recursively).
/// </summary>
/// <remarks>
/// This is all that remains of the emitted `util` surface: the `util` module
/// itself was migrated to <c>stdlib/node/util.ts</c> (pure TS, compiled with
/// the user program), which made the ~4.8k-line emitted twin — format,
/// parseArgs, isDeepStrictEqual, types.*, promisify/callbackify/deprecate
/// wrapper classes and their invocation-dispatch arms — dead IL in every
/// compiled DLL. Only console.dir still consumes UtilInspectValue/Array/Object
/// (2026-07 cleanup audit).
/// </remarks>
public partial class RuntimeEmitter
{
    private void EmitUtilStandaloneMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitUtilInspectValueBody(runtime);
        EmitUtilInspectArrayBody(runtime);
        EmitUtilInspectObjectBody(runtime);
    }
}
