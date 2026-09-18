using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void DefineCancellationFlag(TypeBuilder typeBuilder, EmittedCancellationRuntime cancellation)
    {
        // Cooperative cancellation flag — tripped by the Test262 runner
        // (or any embedder) via reflection to unwind compiled IL on timeout.
        // See issue #74. Public so the runner can SetValue via reflection;
        // polled by volatile loop-backedge reads.
        var cancelRequestedField = typeBuilder.DefineField(
            "_cancelRequested",
            _types.Boolean,
            FieldAttributes.Public | FieldAttributes.Static);
        cancellation.Requested = cancelRequestedField;
    }

    private void EmitCancellationCheck(TypeBuilder typeBuilder, EmittedCancellationRuntime cancellation)
    {
        // CheckCancellation(): if (_cancelRequested) throw new
        //   OperationCanceledException("Compiled execution cancelled.");
        // Used by invocation guards after this declaration stage.
        var checkCancellation = typeBuilder.DefineMethod(
            "CheckCancellation",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
        cancellation.Check = checkCancellation;
        {
            var il = checkCancellation.GetILGenerator();
            var returnLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, cancellation.Requested);
            il.Emit(OpCodes.Brfalse, returnLabel);
            il.Emit(OpCodes.Ldstr, "Compiled execution cancelled.");
            il.Emit(OpCodes.Newobj,
                typeof(OperationCanceledException).GetConstructor([typeof(string)])!);
            il.Emit(OpCodes.Throw);
            il.MarkLabel(returnLabel);
            il.Emit(OpCodes.Ret);
        }
        cancellation.MarkCheckBodyEmitted();
    }

    private void EmitCancellationExceptionFactory(TypeBuilder typeBuilder, EmittedCancellationRuntime cancellation)
    {
        // BuildCancellationException(): constructs and RETURNS (does not throw)
        // the OperationCanceledException used at loop backedges. Loop emitters
        // emit `call BuildCancellationException(); throw` so the cancel path is a
        // non-returning `throw` rather than a returning `call CheckCancellation()`
        // — keeping the hot loop body free of a call that would otherwise force
        // loop-carried doubles onto the stack on SysV x64 (~1.8× on tight numeric
        // loops, #856). See EmittedCancellationRuntime.BuildException.
        var buildCancelEx = typeBuilder.DefineMethod(
            "BuildCancellationException",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(Exception),
            Type.EmptyTypes);
        cancellation.BuildException = buildCancelEx;
        {
            var il = buildCancelEx.GetILGenerator();
            il.Emit(OpCodes.Ldstr, "Compiled execution cancelled.");
            il.Emit(OpCodes.Newobj,
                typeof(OperationCanceledException).GetConstructor([typeof(string)])!);
            il.Emit(OpCodes.Ret);
        }
        cancellation.MarkBuildExceptionBodyEmitted();
    }
}
