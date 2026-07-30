using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits $MapTransformCallback and $FilterTransformCallback helper classes
/// used by Readable.map() and Readable.filter() in compiled mode.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits both $MapTransformCallback and $FilterTransformCallback classes.
    /// Must be called after $Transform is defined (they reference TransformDoneCallback).
    /// </summary>
    private void EmitMapFilterTransformCallbackClasses(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        EmitMapTransformCallbackClass(moduleBuilder, runtime);
        EmitFilterTransformCallbackClass(moduleBuilder, runtime);
    }

    /// <summary>
    /// Emits: public sealed class $MapTransformCallback
    /// Has a single field _fn (the user's map function) and an Invoke(object[] args) method.
    /// Invoke receives (chunk, encoding, done) and calls: done(null, _fn(chunk))
    /// </summary>
    private void EmitMapTransformCallbackClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$MapTransformCallback",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        // Field: _fn (the user's map callback, a $TSFunction)
        var fnField = typeBuilder.DefineField("_fn", _types.Object, FieldAttributes.Private);

        // Constructor: public $MapTransformCallback(object fn)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object]
        );
        runtime.MapTransformCallbackCtor = ctor;

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, fnField);
        ctorIL.Emit(OpCodes.Ret);

        // Invoke(object[] args):
        //   chunk = args[0], done = args[2] as $TransformDoneCallback
        //   result = _fn.Invoke([chunk])
        //   done.Invoke([null, result])
        var invoke = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [_types.ObjectArray]
        );

        var il = invoke.GetILGenerator();
        var chunkLocal = il.DeclareLocal(_types.Object);
        var resultLocal = il.DeclareLocal(_types.Object);
        var doneLocal = il.DeclareLocal(_types.Object);

        // chunk = args[0]
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, chunkLocal);

        // result = _fn.Invoke([chunk])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fnField);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, chunkLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Stloc, resultLocal);

        // done = args[2] (the $TransformDoneCallback)
        var noDoneLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Blt, noDoneLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, doneLocal);

        // done.Invoke([null, result])
        il.Emit(OpCodes.Ldloc, doneLocal);
        il.Emit(OpCodes.Isinst, runtime.TransformDoneCallbackType);
        il.Emit(OpCodes.Brfalse, noDoneLabel);
        il.Emit(OpCodes.Ldloc, doneLocal);
        il.Emit(OpCodes.Castclass, runtime.TransformDoneCallbackType);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldnull); // error = null
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, resultLocal); // data = result
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TransformDoneCallbackInvoke);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noDoneLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits: public sealed class $FilterTransformCallback
    /// Has a single field _fn (the user's filter predicate) and an Invoke(object[] args) method.
    /// Invoke receives (chunk, encoding, done) and calls:
    ///   if (_fn(chunk) is truthy) done(null, chunk) else done(null)
    /// </summary>
    private void EmitFilterTransformCallbackClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$FilterTransformCallback",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );

        var fnField = typeBuilder.DefineField("_fn", _types.Object, FieldAttributes.Private);

        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object]
        );
        runtime.FilterTransformCallbackCtor = ctor;

        var ctorIL = ctor.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, fnField);
        ctorIL.Emit(OpCodes.Ret);

        var invoke = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [_types.ObjectArray]
        );

        var il = invoke.GetILGenerator();
        var chunkLocal = il.DeclareLocal(_types.Object);
        var resultLocal = il.DeclareLocal(_types.Object);
        var doneLocal = il.DeclareLocal(_types.Object);
        var noDoneLabel = il.DefineLabel();
        var notTruthyLabel = il.DefineLabel();
        var afterPushLabel = il.DefineLabel();

        // chunk = args[0]
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, chunkLocal);

        // result = _fn.Invoke([chunk])
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, fnField);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, chunkLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Stloc, resultLocal);

        // Check if done callback exists: args.Length >= 3
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Blt, noDoneLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, doneLocal);
        il.Emit(OpCodes.Ldloc, doneLocal);
        il.Emit(OpCodes.Isinst, runtime.TransformDoneCallbackType);
        il.Emit(OpCodes.Brfalse, noDoneLabel);

        // Check truthiness of result:
        // result is true (bool) OR result is non-zero double
        // if (result is true)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        var checkDoubleLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, checkDoubleLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brtrue, afterPushLabel); // truthy → push chunk
        il.Emit(OpCodes.Br, notTruthyLabel);

        // if (result is double d && d != 0)
        il.MarkLabel(checkDoubleLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notTruthyLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, notTruthyLabel); // d == 0 → not truthy

        // Truthy: done.Invoke([null, chunk])
        il.MarkLabel(afterPushLabel);
        il.Emit(OpCodes.Ldloc, doneLocal);
        il.Emit(OpCodes.Castclass, runtime.TransformDoneCallbackType);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, chunkLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TransformDoneCallbackInvoke);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br, noDoneLabel);

        // Not truthy: done.Invoke([null]) (no data — chunk is filtered out)
        il.MarkLabel(notTruthyLabel);
        il.Emit(OpCodes.Ldloc, doneLocal);
        il.Emit(OpCodes.Castclass, runtime.TransformDoneCallbackType);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, runtime.TransformDoneCallbackInvoke);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(noDoneLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }
}
