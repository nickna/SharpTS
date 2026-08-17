using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the <c>$CallArgsPool</c> class — per-thread arity-keyed pool
    /// of <c>object[]</c> backing arrays for method-call dispatch. Avoids
    /// per-call <c>newarr</c> at <c>obj.method(a, b)</c> sites: instead of
    /// allocating <c>new object[N]</c> on every invocation, we reuse a
    /// thread-static cached array of the right arity.
    ///
    /// Why a separate class instead of fields on <c>$Runtime</c>: at the
    /// time, a layout-sensitive .NET 10 tier-0 JIT bug (issue #39, since
    /// fixed upstream in 10.0.x servicing) could re-trigger when
    /// <c>[ThreadStatic]</c> fields were added to <c>$Runtime</c>, so the
    /// pool went on a fresh class. Harmless to keep it here.
    ///
    /// Safety / aliasing: the call site fills the array, then dispatches
    /// through <c>$TSFunction.Invoke → MethodInvoker.Invoke(target, Span&lt;object?&gt;)</c>.
    /// MethodInvoker reads values into the call frame and does not retain
    /// a reference. Compiled function bodies build their own
    /// <c>arguments</c> object through the shared function-environment prologue
    /// rather than aliasing the args
    /// array, so cross-call reuse is sound.
    ///
    /// Arity ceiling: 1..4 covers the vast majority of real call sites.
    /// Arity 0 doesn't allocate (we never emit <c>new object[0]</c>;
    /// just a shared empty array). Arity ≥ 5 falls back to a fresh
    /// allocation — common-case wins are preserved without growing the
    /// thread-static field list further.
    /// </summary>
    private void EmitCallArgsPool(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$CallArgsPool",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        var threadStaticCtor = typeof(ThreadStaticAttribute).GetConstructor(Type.EmptyTypes)!;
        FieldBuilder DefineField(string name)
        {
            var f = typeBuilder.DefineField(name, _types.ObjectArray,
                FieldAttributes.Private | FieldAttributes.Static);
            f.SetCustomAttribute(threadStaticCtor, CustomAttributeEncoder.EmptyBlob);
            return f;
        }

        var a1 = DefineField("_a1");
        var a2 = DefineField("_a2");
        var a3 = DefineField("_a3");
        var a4 = DefineField("_a4");

        // public static object[] Get(int arity)
        // {
        //     switch (arity) {
        //         case 1: return _a1 ??= new object[1];
        //         case 2: return _a2 ??= new object[2];
        //         case 3: return _a3 ??= new object[3];
        //         case 4: return _a4 ??= new object[4];
        //         default: return new object[arity];
        //     }
        // }
        var method = typeBuilder.DefineMethod(
            "Get",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ObjectArray,
            [_types.Int32]
        );
        var il = method.GetILGenerator();

        var case1 = il.DefineLabel();
        var case2 = il.DefineLabel();
        var case3 = il.DefineLabel();
        var case4 = il.DefineLabel();
        var fallback = il.DefineLabel();

        // arity == 1
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Beq, case1);
        // arity == 2
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Beq, case2);
        // arity == 3
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Beq, case3);
        // arity == 4
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Beq, case4);
        // fallback
        il.Emit(OpCodes.Br, fallback);

        EmitCachedArityCase(il, a1, 1, case1);
        EmitCachedArityCase(il, a2, 2, case2);
        EmitCachedArityCase(il, a3, 3, case3);
        EmitCachedArityCase(il, a4, 4, case4);

        il.MarkLabel(fallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Ret);

        runtime.CallArgsPoolGet = method;
        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits one switch arm of <c>$CallArgsPool.Get</c>: load the field;
    /// if non-null return it; else allocate <c>new object[arity]</c>,
    /// store it, return it.
    /// </summary>
    private void EmitCachedArityCase(ILGenerator il, FieldBuilder field, int arity, Label entryLabel)
    {
        il.MarkLabel(entryLabel);
        // existing = _aN; if (existing != null) return existing;
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Dup);
        var allocLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, allocLabel);
        il.Emit(OpCodes.Ret);

        // Allocate + cache + return.
        il.MarkLabel(allocLabel);
        il.Emit(OpCodes.Pop); // discard the null on stack
        il.Emit(OpCodes.Ldc_I4, arity);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stsfld, field);
        il.Emit(OpCodes.Ret);
    }
}
