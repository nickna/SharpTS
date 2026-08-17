using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits function-related runtime types and helpers.
/// Supports Function.prototype.bind, call, and apply.
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits the $BoundTSFunction class for handling bound functions.
    /// </summary>
    private void EmitBoundTSFunctionClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $BoundTSFunction
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$BoundTSFunction",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.BoundTSFunctionType = typeBuilder;

        // Fields - _target is Assembly since it needs to be accessed by GetFunctionMethod
        var targetField = typeBuilder.DefineField("_target", runtime.TSFunctionType, FieldAttributes.Assembly);
        runtime.BoundTSFunctionTargetField = targetField;
        var thisArgField = typeBuilder.DefineField("_thisArg", _types.Object, FieldAttributes.Private);
        var boundArgsField = typeBuilder.DefineField("_boundArgs", _types.ObjectArray, FieldAttributes.Private);

        // Constructor: public $BoundTSFunction($TSFunction target, object thisArg, object[] boundArgs)
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [runtime.TSFunctionType, _types.Object, _types.ObjectArray]
        );
        runtime.BoundTSFunctionCtor = ctorBuilder;

        var ctorIL = ctorBuilder.GetILGenerator();
        // Call base constructor
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        // this._target = target
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, targetField);
        // this._thisArg = thisArg
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_2);
        ctorIL.Emit(OpCodes.Stfld, thisArgField);
        // this._boundArgs = boundArgs
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_3);
        ctorIL.Emit(OpCodes.Stfld, boundArgsField);
        ctorIL.Emit(OpCodes.Ret);

        // Invoke method: public object Invoke(object[] args)
        var invokeBuilder = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [_types.ObjectArray]
        );
        runtime.BoundTSFunctionInvoke = invokeBuilder;

        var invokeIL = invokeBuilder.GetILGenerator();
        var combinedArgsLocal = invokeIL.DeclareLocal(_types.ObjectArray);
        var boundLenLocal = invokeIL.DeclareLocal(_types.Int32);
        var argsLenLocal = invokeIL.DeclareLocal(_types.Int32);

        // boundLen = _boundArgs?.Length ?? 0
        var noBoundArgsLabel = invokeIL.DefineLabel();
        var afterBoundLenLabel = invokeIL.DefineLabel();
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, boundArgsField);
        invokeIL.Emit(OpCodes.Brfalse, noBoundArgsLabel);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, boundArgsField);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Conv_I4);
        invokeIL.Emit(OpCodes.Stloc, boundLenLocal);
        invokeIL.Emit(OpCodes.Br, afterBoundLenLabel);
        invokeIL.MarkLabel(noBoundArgsLabel);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Stloc, boundLenLocal);
        invokeIL.MarkLabel(afterBoundLenLabel);

        // argsLen = args?.Length ?? 0
        var noArgsLabel = invokeIL.DefineLabel();
        var afterArgsLenLabel = invokeIL.DefineLabel();
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Brfalse, noArgsLabel);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldlen);
        invokeIL.Emit(OpCodes.Conv_I4);
        invokeIL.Emit(OpCodes.Stloc, argsLenLocal);
        invokeIL.Emit(OpCodes.Br, afterArgsLenLabel);
        invokeIL.MarkLabel(noArgsLabel);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Stloc, argsLenLocal);
        invokeIL.MarkLabel(afterArgsLenLabel);

        // combinedArgs = new object[boundLen + argsLen]
        invokeIL.Emit(OpCodes.Ldloc, boundLenLocal);
        invokeIL.Emit(OpCodes.Ldloc, argsLenLocal);
        invokeIL.Emit(OpCodes.Add);
        invokeIL.Emit(OpCodes.Newarr, _types.Object);
        invokeIL.Emit(OpCodes.Stloc, combinedArgsLocal);

        // if (boundLen > 0) Array.Copy(_boundArgs, 0, combinedArgs, 0, boundLen)
        var skipBoundCopyLabel = invokeIL.DefineLabel();
        invokeIL.Emit(OpCodes.Ldloc, boundLenLocal);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Ble, skipBoundCopyLabel);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, boundArgsField);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Ldloc, combinedArgsLocal);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Ldloc, boundLenLocal);
        invokeIL.Emit(OpCodes.Call, _types.GetMethod(_types.ArrayType, "Copy", [_types.ArrayType, _types.Int32, _types.ArrayType, _types.Int32, _types.Int32])!);
        invokeIL.MarkLabel(skipBoundCopyLabel);

        // if (argsLen > 0) Array.Copy(args, 0, combinedArgs, boundLen, argsLen)
        var skipArgsCopyLabel = invokeIL.DefineLabel();
        invokeIL.Emit(OpCodes.Ldloc, argsLenLocal);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Ble, skipArgsCopyLabel);
        invokeIL.Emit(OpCodes.Ldarg_1);
        invokeIL.Emit(OpCodes.Ldc_I4_0);
        invokeIL.Emit(OpCodes.Ldloc, combinedArgsLocal);
        invokeIL.Emit(OpCodes.Ldloc, boundLenLocal);
        invokeIL.Emit(OpCodes.Ldloc, argsLenLocal);
        invokeIL.Emit(OpCodes.Call, _types.GetMethod(_types.ArrayType, "Copy", [_types.ArrayType, _types.Int32, _types.ArrayType, _types.Int32, _types.Int32])!);
        invokeIL.MarkLabel(skipArgsCopyLabel);

        // return _target.InvokeWithThis(_thisArg, combinedArgs)
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, targetField);
        invokeIL.Emit(OpCodes.Ldarg_0);
        invokeIL.Emit(OpCodes.Ldfld, thisArgField);
        invokeIL.Emit(OpCodes.Ldloc, combinedArgsLocal);
        invokeIL.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        invokeIL.Emit(OpCodes.Ret);

        // InvokeWithThis method: public object InvokeWithThis(object thisArg, object[] args)
        // For bound functions, the original thisArg is ignored (bound this takes precedence)
        var invokeWithThisBuilder = typeBuilder.DefineMethod(
            "InvokeWithThis",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.ObjectArray]
        );
        runtime.BoundTSFunctionInvokeWithThis = invokeWithThisBuilder;

        var iwtIL = invokeWithThisBuilder.GetILGenerator();
        // Just call Invoke(args) - the bound this is already set
        iwtIL.Emit(OpCodes.Ldarg_0);
        iwtIL.Emit(OpCodes.Ldarg_2);
        iwtIL.Emit(OpCodes.Callvirt, invokeBuilder);
        iwtIL.Emit(OpCodes.Ret);

        // ToString
        var toStringBuilder = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        var toStringIL = toStringBuilder.GetILGenerator();
        toStringIL.Emit(OpCodes.Ldstr, "[Function: bound]");
        toStringIL.Emit(OpCodes.Ret);

        // Finalize the type
        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits the $BoundAnyFunction class — a partial-application wrapper used by
    /// <c>Function.prototype.bind</c> when the bind receiver is NOT a <c>$TSFunction</c>
    /// (e.g. a <c>$BoundArrayMethod</c> / <c>$BoundMapMethod</c> / <c>$BoundSetMethod</c>,
    /// a <c>$BoundTSFunction</c>, or any other callable that <c>InvokeValue</c> knows
    /// how to dispatch). Stores (target, boundArgs) and, on invocation, prepends
    /// boundArgs to the call args and dispatches to the target. <c>thisArg</c> is
    /// ignored because bound methods already capture their receiver.
    /// </summary>
    /// <remarks>
    /// Must be emitted AFTER <c>$TSFunction</c>, <c>$BoundTSFunction</c>, and all three
    /// <c>$Bound*Method</c> TypeBuilders have been defined (Phase 1), because the
    /// emitted body of <c>Invoke</c> <c>Callvirt</c>s each of their <c>Invoke</c>
    /// MethodBuilders directly — there is no <c>InvokeValue</c> yet at this point in
    /// the emission pipeline.
    /// </remarks>
    private void EmitBoundAnyFunctionClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$BoundAnyFunction",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.BoundAnyFunctionType = typeBuilder;

        var targetField = typeBuilder.DefineField("_target", _types.Object, FieldAttributes.Private);
        var boundArgsField = typeBuilder.DefineField("_boundArgs", _types.ObjectArray, FieldAttributes.Private);

        // ctor(object target, object[] boundArgs)
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object, _types.ObjectArray]
        );
        runtime.BoundAnyFunctionCtor = ctorBuilder;

        var ctorIL = ctorBuilder.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, targetField);
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_2);
        ctorIL.Emit(OpCodes.Stfld, boundArgsField);
        ctorIL.Emit(OpCodes.Ret);

        // Invoke(object[] args): prepends boundArgs, then dispatches to target by type.
        var invokeBuilder = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [_types.ObjectArray]
        );
        runtime.BoundAnyFunctionInvoke = invokeBuilder;

        var il = invokeBuilder.GetILGenerator();
        var combinedLocal = il.DeclareLocal(_types.ObjectArray);
        var boundLenLocal = il.DeclareLocal(_types.Int32);
        var argsLenLocal = il.DeclareLocal(_types.Int32);

        // boundLen = _boundArgs?.Length ?? 0
        var noBoundLabel = il.DefineLabel();
        var afterBoundLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, boundArgsField);
        il.Emit(OpCodes.Brfalse, noBoundLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, boundArgsField);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, boundLenLocal);
        il.Emit(OpCodes.Br, afterBoundLabel);
        il.MarkLabel(noBoundLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, boundLenLocal);
        il.MarkLabel(afterBoundLabel);

        // argsLen = args?.Length ?? 0
        var noArgsLabel = il.DefineLabel();
        var afterArgsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noArgsLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, argsLenLocal);
        il.Emit(OpCodes.Br, afterArgsLabel);
        il.MarkLabel(noArgsLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, argsLenLocal);
        il.MarkLabel(afterArgsLabel);

        // combined = new object[boundLen + argsLen]
        il.Emit(OpCodes.Ldloc, boundLenLocal);
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, combinedLocal);

        // if (boundLen > 0) Array.Copy(_boundArgs, 0, combined, 0, boundLen)
        var skipBoundCopyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, boundLenLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, skipBoundCopyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, boundArgsField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, combinedLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, boundLenLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.ArrayType, "Copy", [_types.ArrayType, _types.Int32, _types.ArrayType, _types.Int32, _types.Int32])!);
        il.MarkLabel(skipBoundCopyLabel);

        // if (argsLen > 0) Array.Copy(args, 0, combined, boundLen, argsLen)
        var skipArgsCopyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, skipArgsCopyLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, combinedLocal);
        il.Emit(OpCodes.Ldloc, boundLenLocal);
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.ArrayType, "Copy", [_types.ArrayType, _types.Int32, _types.ArrayType, _types.Int32, _types.Int32])!);
        il.MarkLabel(skipArgsCopyLabel);

        // Dispatch chain: isinst target against known callables, Callvirt each one's Invoke
        EmitDispatchToTarget(il, runtime, targetField, combinedLocal);

        // Fall-through: unknown target type → return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // InvokeWithThis(object thisArg, object[] args): bound target ignores thisArg
        var invokeWithThisBuilder = typeBuilder.DefineMethod(
            "InvokeWithThis",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.ObjectArray]
        );
        _ = invokeWithThisBuilder;

        var iwtIL = invokeWithThisBuilder.GetILGenerator();
        iwtIL.Emit(OpCodes.Ldarg_0);
        iwtIL.Emit(OpCodes.Ldarg_2);
        iwtIL.Emit(OpCodes.Callvirt, invokeBuilder);
        iwtIL.Emit(OpCodes.Ret);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits a dispatch chain against an <c>object</c>-typed target field, calling the
    /// appropriate <c>Invoke</c> method on each known runtime callable type. Used by
    /// <c>$BoundAnyFunction</c>, <c>$FunctionCallWrapper</c>, and <c>$FunctionApplyWrapper</c>
    /// to invoke a target whose concrete type isn't known at emission time.
    ///
    /// The chain handles: <c>$TSFunction</c>, <c>$BoundTSFunction</c>,
    /// <c>$BoundArrayMethod</c>, <c>$BoundMapMethod</c>, <c>$BoundSetMethod</c>,
    /// <c>$BoundAnyFunction</c> (recursive bind). The caller must emit the args array
    /// and load it into <paramref name="argsLocal"/> before calling this helper.
    /// After dispatch, each branch emits <c>Ret</c>, so the caller is responsible for
    /// a fall-through path (e.g. <c>ldnull; ret</c>).
    /// </summary>
    private void EmitDispatchToTarget(
        ILGenerator il,
        EmittedRuntime runtime,
        FieldBuilder targetField,
        LocalBuilder argsLocal,
        LocalBuilder? thisArgLocal = null)
    {
        // $TSFunction → target.Invoke(args)
        var notTSFunctionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notTSFunctionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvoke);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTSFunctionLabel);

        // $BoundTSFunction → target.Invoke(args)
        var notBoundTSFunctionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brfalse, notBoundTSFunctionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvoke);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBoundTSFunctionLabel);

        // $BoundArrayMethod → target.Invoke(args)
        var notBAMLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Isinst, runtime.BoundArrayMethodType);
        il.Emit(OpCodes.Brfalse, notBAMLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Castclass, runtime.BoundArrayMethodType);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.BoundArrayMethodInvoke);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBAMLabel);

        // $BoundMapMethod → target.Invoke(args). Gated on UsesMap (the wrapper
        // type only exists when EmitBoundMapMethod{TypeDefinition,Finalize} run).
        var notBMMLabel = il.DefineLabel();
        if (_features.UsesMap)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, targetField);
            il.Emit(OpCodes.Isinst, runtime.BoundMapMethodType);
            il.Emit(OpCodes.Brfalse, notBMMLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, targetField);
            il.Emit(OpCodes.Castclass, runtime.BoundMapMethodType);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Callvirt, runtime.BoundMapMethodInvoke);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notBMMLabel);
        }

        // $BoundSetMethod → target.Invoke(args). Gated on UsesSet.
        var notBSMLabel = il.DefineLabel();
        if (_features.UsesSet)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, targetField);
            il.Emit(OpCodes.Isinst, runtime.BoundSetMethodType);
            il.Emit(OpCodes.Brfalse, notBSMLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, targetField);
            il.Emit(OpCodes.Castclass, runtime.BoundSetMethodType);
            il.Emit(OpCodes.Ldloc, argsLocal);
            il.Emit(OpCodes.Callvirt, runtime.BoundSetMethodInvoke);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notBSMLabel);
        }

        // $BoundAnyFunction → target.Invoke(args) (recursive chained .bind)
        var notBAFLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Isinst, runtime.BoundAnyFunctionType);
        il.Emit(OpCodes.Brfalse, notBAFLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Castclass, runtime.BoundAnyFunctionType);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Callvirt, runtime.BoundAnyFunctionInvoke);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBAFLabel);

        if (thisArgLocal != null)
            EmitProxyInvokeWithThisFallback(
                il, targetField, thisArgLocal, argsLocal);
    }

    private void EmitProxyInvokeWithThisFallback(
        ILGenerator il,
        FieldBuilder targetField,
        LocalBuilder thisArgLocal,
        LocalBuilder argsLocal)
    {
        var proxyLabel = il.DefineLabel();
        var notProxyLabel = il.DefineLabel();
        EmitProxyTypeCheck(
            il,
            () =>
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, targetField);
            },
            proxyLabel,
            notProxyLabel);

        il.MarkLabel(proxyLabel);
        var methodLocal = il.DeclareLocal(_types.MethodInfo);
        var resultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "InvokeWithThis");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Stloc, methodLocal);

        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, thisArgLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(
            _types.MethodBase, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.BeginCatchBlock(_types.TargetInvocationException);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(
            _types.Exception, "InnerException").GetGetMethod()!);
        var innerLocal = il.DeclareLocal(_types.Exception);
        il.Emit(OpCodes.Stloc, innerLocal);
        var rethrowOriginal = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Brfalse, rethrowOriginal);
        il.Emit(OpCodes.Ldloc, innerLocal);
        il.Emit(OpCodes.Throw);
        il.MarkLabel(rethrowOriginal);
        il.Emit(OpCodes.Rethrow);
        il.EndExceptionBlock();
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notProxyLabel);
    }

    /// <summary>
    /// Emits the GetFunctionMethod helper that returns bind/call/apply wrappers.
    /// </summary>
    private void EmitGetFunctionMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // GetFunctionMethod(object func, string methodName) -> object
        // Functions/arrows are ordinary objects: a missing property reads as JS
        // `undefined`, not CLR null (#651). The miss path below returns the
        // undefined sentinel so `typeof fn.absent === "undefined"` matches both
        // the interpreter and plain compiled objects.
        var method = typeBuilder.DefineMethod(
            "GetFunctionMethod",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.String]
        );
        runtime.GetFunctionMethod = method;

        var il = method.GetILGenerator();
        var missLabel = il.DefineLabel();
        var bindLabel = il.DefineLabel();
        var callLabel = il.DefineLabel();
        var applyLabel = il.DefineLabel();
        var lengthLabel = il.DefineLabel();
        var nameLabel = il.DefineLabel();

        // if (func == null) treat as a miss (returns undefined) — defensive;
        // callers only reach here after an Isinst confirms a function receiver.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, missLabel);

        // Check for "bind"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "bind");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, bindLabel);

        // Check for "call"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "call");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, callLabel);

        // Check for "apply"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "apply");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, applyLabel);

        // Check for "length"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, lengthLabel);

        // Check for "name"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brtrue, nameLabel);

        // Check for inherited Object.prototype methods — return a $TSFunction
        // whose target is `func` (the receiver) and whose method is the
        // appropriate helper. When invoked with [name], $TSFunction.Invoke
        // prepends target → calls Helper(func, name). Required for Test262
        // tests that probe `String.prototype.X.hasOwnProperty("length")`,
        // `RegExp.prototype.exec.propertyIsEnumerable("length")` etc.
        void EmitProtoMethodCheck(string jsName, MethodBuilder helper)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, skipLabel);
            il.Emit(OpCodes.Ldarg_0);
            _types.EmitLoadMethodInfo(il, helper);
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(skipLabel);
        }
        EmitProtoMethodCheck("hasOwnProperty",       runtime.HasOwnPropertyHelperMethod);
        EmitProtoMethodCheck("propertyIsEnumerable", runtime.PropertyIsEnumerableHelperMethod);
        EmitProtoMethodCheck("isPrototypeOf",        runtime.IsPrototypeOfHelperMethod);

        // Fallback: check for a user-assigned property via PropertyDescriptorStore.
        // JS functions are objects and can carry arbitrary properties (`fn.x = 42`). Compiled
        // SetProperty routes $TSFunction writes into PDS as data descriptors; we mirror that
        // here on the read side. Returns descriptor.Value if present, otherwise falls through
        // to null. Enables patterns like `lodash.chunk = fn; lodash.chunk(...)`.
        var pdsDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, pdsDescLocal);
        il.Emit(OpCodes.Ldloc, pdsDescLocal);
        var checkProtoLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, checkProtoLabel);

        // Descriptor-backed function properties obey ordinary [[Get]]. In
        // particular, Object.defineProperty(fn, "x", { get() { ... } }) must
        // invoke the getter with fn as `this`; returning Descriptor.Value here
        // made accessor-only properties look like null and broke generic
        // consumers such as Object.defineProperties.
        var functionPdsGetterLocal = il.DeclareLocal(_types.Object);
        var functionPdsDataLabel = il.DefineLabel();
        var functionPdsUndefinedAccessorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, pdsDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, functionPdsGetterLocal);
        il.Emit(OpCodes.Ldloc, functionPdsGetterLocal);
        il.Emit(OpCodes.Brfalse, functionPdsDataLabel);
        il.Emit(OpCodes.Ldloc, functionPdsGetterLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, functionPdsUndefinedAccessorLabel);
        var functionPdsBoundGetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, functionPdsGetterLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, functionPdsBoundGetterLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(functionPdsBoundGetterLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, functionPdsGetterLocal);
        il.Emit(OpCodes.Castclass, runtime.BoundAnyFunctionType);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.BoundAnyFunctionInvoke);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(functionPdsDataLabel);
        // A setter-only descriptor is still an accessor descriptor; [[Get]]
        // returns undefined rather than the descriptor's unused Value slot.
        il.Emit(OpCodes.Ldloc, pdsDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, functionPdsUndefinedAccessorLabel);
        il.Emit(OpCodes.Ldloc, pdsDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(functionPdsUndefinedAccessorLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        // PDS lookup failed. Per JS spec every function auto-creates an empty
        // `.prototype` object on first read, with identity stable across
        // references: `fn.prototype === fn.prototype` must be true. The
        // MethodInfo-keyed `_prototypeCache` ensures any two $TSFunction
        // wrappers for the same underlying method return the same prototype
        // object — without this, `Foo.prototype.x = v` silently no-ops
        // because each Foo reference creates a fresh $TSFunction with its
        // own PDS key.
        il.MarkLabel(checkProtoLabel);
        // ECMA-262 §20.2.3: every Function instance inherits from
        // %Function.prototype%. `fn.constructor` walks the proto chain to
        // Function.prototype.constructor === Function (= typeof($TSFunction)).
        // The PDS lookup above already handles user overrides
        // (`fn.constructor = X`), so we only reach this when the user hasn't
        // set their own. Required for Test262 patterns like
        // `Object(fn).constructor === Function`.
        var notConstructorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notConstructorLabel);
        il.Emit(OpCodes.Call, runtime.FunctionPrototypePopulateMethod);
        var ctorSlotLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.FunctionPrototypeField);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Ldloca, ctorSlotLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            _types.String, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, ctorSlotLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notConstructorLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "prototype");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var fnProtoFallbackLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, fnProtoFallbackLabel);

        // Only auto-create for actual $TSFunction callees (has an inner _method
        // field). Other callable shapes ($BoundArrayMethod, $FunctionCallWrapper,
        // etc.) don't have a meaningful prototype and fall through to null.
        var tsfuncLocal = il.DeclareLocal(runtime.TSFunctionType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, tsfuncLocal);
        il.Emit(OpCodes.Brfalse, missLabel);

        // methodKey = tsfuncLocal.GetMethodInfo()  (public getter — avoids
        // field-access violation from accessing private _method across
        // TypeBuilder boundaries at JIT time).
        var prototypeCacheType = runtime.TSFunctionPrototypeCacheField.FieldType;
        var tryGetValueM = _types.GetMethod(prototypeCacheType, "TryGetValue", [_types.MethodInfo, _types.Object.MakeByRefType()])!;
        var prototypeCacheGetOrAdd = _types.GetMethods(prototypeCacheType)
            .First(m => m.Name == "GetOrAdd"
                 && m.GetParameters().Length == 2
                 && m.GetParameters()[1].ParameterType == _types.Object);

        var cachedProto = il.DeclareLocal(_types.Object);
        var newProto = il.DeclareLocal(runtime.TSObjectType);
        var methodKeyLocal = il.DeclareLocal(_types.MethodInfo);
        il.Emit(OpCodes.Ldloc, tsfuncLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionGetMethodInfo);
        il.Emit(OpCodes.Stloc, methodKeyLocal);

        // Built-in helpers ($TSFunction wrapping $Runtime methods) do NOT
        // get an auto-created `prototype` per ECMA-262 — they're not
        // constructors. Test262 patterns like
        // `String.prototype.charAt.prototype === undefined` rely on this.
        // Detect via DeclaringType.Name == "$Runtime".
        var notRuntimeMethodLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, methodKeyLocal);
        il.Emit(OpCodes.Brfalse, notRuntimeMethodLabel);
        il.Emit(OpCodes.Ldloc, methodKeyLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(typeof(System.Reflection.MemberInfo), "DeclaringType").GetGetMethod()!);
        var dtLocal = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Stloc, dtLocal);
        il.Emit(OpCodes.Ldloc, dtLocal);
        il.Emit(OpCodes.Brfalse, notRuntimeMethodLabel);
        il.Emit(OpCodes.Ldloc, dtLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(typeof(System.Reflection.MemberInfo), "Name").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "$Runtime");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notRuntimeMethodLabel);
        // Built-in: return undefined (no auto-created prototype).
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notRuntimeMethodLabel);

        // if (_prototypeCache.TryGetValue(methodKey, out cached)) return cached
        il.Emit(OpCodes.Ldsfld, runtime.TSFunctionPrototypeCacheField);
        il.Emit(OpCodes.Ldloc, methodKeyLocal);
        il.Emit(OpCodes.Ldloca, cachedProto);
        il.Emit(OpCodes.Callvirt, tryGetValueM);
        var needCreate = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, needCreate);
        il.Emit(OpCodes.Ldloc, cachedProto);
        il.Emit(OpCodes.Ret);

        // Create a fresh $Object and GetOrAdd it (handles races).
        // ECMA-262 §15.2.2.1: every auto-created Function.prototype carries
        // `constructor: theFunction` so `(new F()).constructor === F` holds.
        // Requires that all callers reach the function-name binding through
        // TSFunctionGetOrCreate (the cached canonical wrapper) so the
        // identity stored here matches the variable-bound F.
        il.MarkLabel(needCreate);
        var protoDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, protoDictLocal);
        il.Emit(OpCodes.Ldloc, protoDictLocal);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));
        il.Emit(OpCodes.Ldloc, protoDictLocal);
        il.Emit(OpCodes.Newobj, runtime.TSObjectCtor);
        il.Emit(OpCodes.Stloc, newProto);

        il.Emit(OpCodes.Ldsfld, runtime.TSFunctionPrototypeCacheField);
        il.Emit(OpCodes.Ldloc, methodKeyLocal);
        il.Emit(OpCodes.Ldloc, newProto);
        il.Emit(OpCodes.Callvirt, prototypeCacheGetOrAdd);
        il.Emit(OpCodes.Ret);

        // bind: return new $FunctionBindWrapper(func)
        il.MarkLabel(bindLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.FunctionBindWrapperCtor);
        il.Emit(OpCodes.Ret);

        // call: return new $FunctionCallWrapper(func)
        il.MarkLabel(callLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.FunctionCallWrapperCtor);
        il.Emit(OpCodes.Ret);

        // apply: return new $FunctionApplyWrapper(func)
        il.MarkLabel(applyLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, runtime.FunctionApplyWrapperCtor);
        il.Emit(OpCodes.Ret);

        // length: check if func is $TSFunction and call get_Length
        il.MarkLabel(lengthLabel);
        var lengthNotTSFunctionLabel = il.DefineLabel();
        var lengthEndLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, lengthNotTSFunctionLabel);
        // If `length` was deleted on this $TSFunction, return undefined
        // instead of the cached spec value (ECMA-262 §17 configurable).
        var lengthDeletedSkipLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, runtime.IsBuiltinDeletedMethod);
        il.Emit(OpCodes.Brfalse, lengthDeletedSkipLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(lengthDeletedSkipLabel);
        // It's a $TSFunction - call get_Length()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Call, runtime.TSFunctionLengthGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Br, lengthEndLabel);
        // Not a TSFunction - return 0.0
        il.MarkLabel(lengthNotTSFunctionLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.MarkLabel(lengthEndLabel);
        il.Emit(OpCodes.Ret);

        // name: check if func is $TSFunction or $BoundTSFunction
        il.MarkLabel(nameLabel);
        var nameIsTSFunctionLabel = il.DefineLabel();
        var nameIsBoundLabel = il.DefineLabel();
        var nameEndLabel = il.DefineLabel();

        // Check for $TSFunction
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, nameIsTSFunctionLabel);

        // Check for $BoundTSFunction
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, nameIsBoundLabel);

        // Unknown - return ""
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Br, nameEndLabel);

        // It's a $TSFunction. If `name` was deleted on this instance, the
        // property is gone — return undefined instead of the cached value.
        il.MarkLabel(nameIsTSFunctionLabel);
        var nameDeletedSkipLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, runtime.IsBuiltinDeletedMethod);
        il.Emit(OpCodes.Brfalse, nameDeletedSkipLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(nameDeletedSkipLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Call, runtime.TSFunctionNameGetter);
        il.Emit(OpCodes.Br, nameEndLabel);

        // It's a $BoundTSFunction - get "bound " + target.Name
        // Note: We need to get the _target field and call its get_Name()
        // But _target is $TSFunction type, so we can call get_Name() on it
        il.MarkLabel(nameIsBoundLabel);
        il.Emit(OpCodes.Ldstr, "bound ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Ldfld, runtime.BoundTSFunctionTargetField);
        il.Emit(OpCodes.Call, runtime.TSFunctionNameGetter);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));

        il.MarkLabel(nameEndLabel);
        il.Emit(OpCodes.Ret);

        // Fallback: walk Function.prototype dict for user-installed properties.
        // ECMA-262 §20.2.3 — every function inherits from Function.prototype.
        // Test262 patterns: `Function.prototype.X = v; fn.X` must surface v.
        // Skipped for "prototype" because the auto-create logic above handles
        // it (and we don't want user-set Function.prototype.prototype to
        // shadow that).
        il.MarkLabel(fnProtoFallbackLabel);
        il.Emit(OpCodes.Call, runtime.FunctionPrototypePopulateMethod);
        var functionPrototypeDescriptorLocal = il.DeclareLocal(
            runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldsfld, runtime.FunctionPrototypeField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, functionPrototypeDescriptorLocal);
        var noFunctionPrototypeDescriptorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, functionPrototypeDescriptorLocal);
        il.Emit(OpCodes.Brfalse, noFunctionPrototypeDescriptorLabel);
        var functionPrototypeGetterLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, functionPrototypeDescriptorLocal);
        il.Emit(OpCodes.Callvirt,
            runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, functionPrototypeGetterLocal);
        var functionPrototypeDataLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, functionPrototypeGetterLocal);
        il.Emit(OpCodes.Brfalse, functionPrototypeDataLabel);
        il.Emit(OpCodes.Ldloc, functionPrototypeGetterLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        var functionPrototypeUndefinedLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, functionPrototypeUndefinedLabel);
        var functionPrototypeBoundGetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, functionPrototypeGetterLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, functionPrototypeBoundGetterLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(functionPrototypeBoundGetterLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, functionPrototypeGetterLocal);
        il.Emit(OpCodes.Castclass, runtime.BoundAnyFunctionType);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Callvirt, runtime.BoundAnyFunctionInvoke);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(functionPrototypeDataLabel);
        il.Emit(OpCodes.Ldloc, functionPrototypeDescriptorLocal);
        il.Emit(OpCodes.Callvirt,
            runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, functionPrototypeUndefinedLabel);
        il.Emit(OpCodes.Ldloc, functionPrototypeDescriptorLocal);
        il.Emit(OpCodes.Callvirt,
            runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(functionPrototypeUndefinedLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noFunctionPrototypeDescriptorLabel);
        var fpSlotLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.FunctionPrototypeField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, fpSlotLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            _types.String, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, missLabel);
        il.Emit(OpCodes.Ldloc, fpSlotLocal);
        il.Emit(OpCodes.Ret);

        // Miss: a function/arrow has no such property. JS functions are
        // ordinary objects, so an absent key reads as `undefined`, not null
        // (#651). Reached for a generic missing property, `prototype` on a
        // prototype-less callable (bound functions / built-in method wrappers),
        // and the defensive null-func guard.
        il.MarkLabel(missLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the $FunctionBindWrapper class.
    /// When invoked, creates a $BoundTSFunction.
    /// </summary>
    private void EmitFunctionBindWrapperClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$FunctionBindWrapper",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.FunctionBindWrapperType = typeBuilder;

        var targetField = typeBuilder.DefineField("_target", _types.Object, FieldAttributes.Private);

        // Constructor
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object]
        );
        runtime.FunctionBindWrapperCtor = ctorBuilder;

        var ctorIL = ctorBuilder.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, targetField);
        ctorIL.Emit(OpCodes.Ret);

        // Invoke: object Invoke(object[] args)
        // args[0] = thisArg, args[1..] = bound args
        var invokeBuilder = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [_types.ObjectArray]
        );
        runtime.FunctionBindWrapperInvoke = invokeBuilder;

        var il = invokeBuilder.GetILGenerator();
        var thisArgLocal = il.DeclareLocal(_types.Object);
        var boundArgsLocal = il.DeclareLocal(_types.ObjectArray);
        var argsLenLocal = il.DeclareLocal(_types.Int32);

        // argsLen = args?.Length ?? 0
        var noArgsLabel = il.DefineLabel();
        var afterArgsLenLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noArgsLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, argsLenLocal);
        il.Emit(OpCodes.Br, afterArgsLenLabel);
        il.MarkLabel(noArgsLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, argsLenLocal);
        il.MarkLabel(afterArgsLenLabel);

        // thisArg = argsLen > 0 ? args[0] : null
        var noThisLabel = il.DefineLabel();
        var afterThisLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, noThisLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, thisArgLocal);
        il.Emit(OpCodes.Br, afterThisLabel);
        il.MarkLabel(noThisLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, thisArgLocal);
        il.MarkLabel(afterThisLabel);

        // boundArgs = argsLen > 1 ? args[1..] : empty array
        var noBoundArgsLabel = il.DefineLabel();
        var afterBoundArgsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ble, noBoundArgsLabel);

        // boundArgs = new object[argsLen - 1]
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, boundArgsLocal);

        // Array.Copy(args, 1, boundArgs, 0, argsLen - 1)
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, boundArgsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.ArrayType, "Copy", [_types.ArrayType, _types.Int32, _types.ArrayType, _types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Br, afterBoundArgsLabel);

        il.MarkLabel(noBoundArgsLabel);
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(_types.GetMethod(typeof(Array), "Empty"), _types.Object));
        il.Emit(OpCodes.Stloc, boundArgsLocal);
        il.MarkLabel(afterBoundArgsLabel);

        // Dispatch based on target type:
        //   $TSFunction → $BoundTSFunction (keeps thisArg + boundArgs)
        //   $BoundArrayMethod + array thisArg → a wrapper rebound to thisArg
        //   anything else callable → $BoundAnyFunction (partial-apply only)
        // Array method values are represented by receiver-capturing wrappers as
        // an implementation detail, but Function.prototype.bind must still honor
        // its explicit thisArg (for example, `[].flat.bind(otherArray)()`).
        var isTSFunctionLabel = il.DefineLabel();
        var isBoundArrayMethodLabel = il.DefineLabel();
        var otherCallableLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, isTSFunctionLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Isinst, runtime.BoundArrayMethodType);
        il.Emit(OpCodes.Brfalse, otherCallableLabel);
        il.Emit(OpCodes.Ldloc, thisArgLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, isBoundArrayMethodLabel);

        // Non-TSFunction target: new $BoundAnyFunction(target, boundArgs)
        il.MarkLabel(otherCallableLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Ldloc, boundArgsLocal);
        il.Emit(OpCodes.Newobj, runtime.BoundAnyFunctionCtor);
        il.Emit(OpCodes.Ret);

        // Recreate the implementation wrapper with the explicit bound receiver,
        // then retain the ordinary bound-argument concatenation behavior.
        il.MarkLabel(isBoundArrayMethodLabel);
        il.Emit(OpCodes.Ldloc, thisArgLocal);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Castclass, runtime.BoundArrayMethodType);
        il.Emit(OpCodes.Ldfld, runtime.BoundArrayMethodNameField);
        il.Emit(OpCodes.Newobj, runtime.BoundArrayMethodCtor);
        il.Emit(OpCodes.Ldloc, boundArgsLocal);
        il.Emit(OpCodes.Newobj, runtime.BoundAnyFunctionCtor);
        il.Emit(OpCodes.Ret);

        // return new $BoundTSFunction(($TSFunction)_target, thisArg, boundArgs)
        il.MarkLabel(isTSFunctionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldloc, thisArgLocal);
        il.Emit(OpCodes.Ldloc, boundArgsLocal);
        il.Emit(OpCodes.Newobj, runtime.BoundTSFunctionCtor);
        il.Emit(OpCodes.Ret);

        // InvokeWithThis (not used but needed for consistency)
        var iwtBuilder = typeBuilder.DefineMethod(
            "InvokeWithThis",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.ObjectArray]
        );
        var iwtIL = iwtBuilder.GetILGenerator();
        iwtIL.Emit(OpCodes.Ldarg_0);
        iwtIL.Emit(OpCodes.Ldarg_2);
        iwtIL.Emit(OpCodes.Callvirt, invokeBuilder);
        iwtIL.Emit(OpCodes.Ret);

        // Finalize the type
        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits the $FunctionCallWrapper class.
    /// When invoked, calls the target function with the specified this and args.
    /// </summary>
    private void EmitFunctionCallWrapperClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$FunctionCallWrapper",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.FunctionCallWrapperType = typeBuilder;

        var targetField = typeBuilder.DefineField("_target", _types.Object, FieldAttributes.Private);

        // Constructor
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object]
        );
        runtime.FunctionCallWrapperCtor = ctorBuilder;

        var ctorIL = ctorBuilder.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, targetField);
        ctorIL.Emit(OpCodes.Ret);

        // Invoke: object Invoke(object[] args)
        // args[0] = thisArg, args[1..] = call args
        var invokeBuilder = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [_types.ObjectArray]
        );
        runtime.FunctionCallWrapperInvoke = invokeBuilder;

        var il = invokeBuilder.GetILGenerator();
        var thisArgLocal = il.DeclareLocal(_types.Object);
        var callArgsLocal = il.DeclareLocal(_types.ObjectArray);
        var argsLenLocal = il.DeclareLocal(_types.Int32);

        // argsLen = args?.Length ?? 0
        var noArgsLabel = il.DefineLabel();
        var afterArgsLenLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noArgsLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, argsLenLocal);
        il.Emit(OpCodes.Br, afterArgsLenLabel);
        il.MarkLabel(noArgsLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, argsLenLocal);
        il.MarkLabel(afterArgsLenLabel);

        // thisArg = argsLen > 0 ? args[0] : null
        var noThisLabel = il.DefineLabel();
        var afterThisLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, noThisLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, thisArgLocal);
        il.Emit(OpCodes.Br, afterThisLabel);
        il.MarkLabel(noThisLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, thisArgLocal);
        il.MarkLabel(afterThisLabel);

        // callArgs = argsLen > 1 ? args[1..] : empty array
        var noCallArgsLabel = il.DefineLabel();
        var afterCallArgsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ble, noCallArgsLabel);

        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, callArgsLocal);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, callArgsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.ArrayType, "Copy", [_types.ArrayType, _types.Int32, _types.ArrayType, _types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Br, afterCallArgsLabel);

        il.MarkLabel(noCallArgsLabel);
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(_types.GetMethod(typeof(Array), "Empty"), _types.Object));
        il.Emit(OpCodes.Stloc, callArgsLocal);
        il.MarkLabel(afterCallArgsLabel);

        // Check target type and invoke. TSFunction/BoundTSFunction honor the
        // thisArg via InvokeWithThis. Collection/array method wrappers capture
        // the receiver only as an implementation detail; Function.prototype.call
        // must rebind them to its explicit thisArg just like native methods.
        var isTSFunctionLabel = il.DefineLabel();
        var isBoundTSFunctionLabel = il.DefineLabel();
        var isBoundArrayMethodLabel = il.DefineLabel();
        var isBoundMapMethodLabel = il.DefineLabel();
        var isBoundSetMethodLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, isTSFunctionLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, isBoundTSFunctionLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Isinst, runtime.BoundArrayMethodType);
        var notBoundArrayMethodLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notBoundArrayMethodLabel);
        il.Emit(OpCodes.Ldloc, thisArgLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, isBoundArrayMethodLabel);
        il.MarkLabel(notBoundArrayMethodLabel);

        if (_features.UsesMap)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, targetField);
            il.Emit(OpCodes.Isinst, runtime.BoundMapMethodType);
            var notBoundMapMethodLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, notBoundMapMethodLabel);
            il.Emit(OpCodes.Ldloc, thisArgLocal);
            il.Emit(OpCodes.Isinst, _types.DictionaryObjectObject);
            il.Emit(OpCodes.Brtrue, isBoundMapMethodLabel);
            il.MarkLabel(notBoundMapMethodLabel);
        }

        if (_features.UsesSet)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, targetField);
            il.Emit(OpCodes.Isinst, runtime.BoundSetMethodType);
            var notBoundSetMethodLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, notBoundSetMethodLabel);
            il.Emit(OpCodes.Ldloc, thisArgLocal);
            il.Emit(OpCodes.Isinst, _types.HashSetOfObject);
            il.Emit(OpCodes.Brtrue, isBoundSetMethodLabel);
            il.MarkLabel(notBoundSetMethodLabel);
        }

        // Remaining non-TSFunction callables, including proxies, retain the
        // explicit thisArg through the shared dispatch fallback.
        EmitDispatchToTarget(
            il, runtime, targetField, callArgsLocal, thisArgLocal);

        // Fall-through: unknown target type → return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // return (($TSFunction)_target).InvokeWithThis(thisArg, callArgs)
        il.MarkLabel(isTSFunctionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldloc, thisArgLocal);
        il.Emit(OpCodes.Ldloc, callArgsLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Ret);

        // return (($BoundTSFunction)_target).InvokeWithThis(thisArg, callArgs)
        il.MarkLabel(isBoundTSFunctionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Ldloc, thisArgLocal);
        il.Emit(OpCodes.Ldloc, callArgsLocal);
        il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvokeWithThis);
        il.Emit(OpCodes.Ret);

        // Recreate the lightweight method wrapper with call's explicit
        // receiver, preserving the captured JavaScript method name.
        il.MarkLabel(isBoundArrayMethodLabel);
        il.Emit(OpCodes.Ldloc, thisArgLocal);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Castclass, runtime.BoundArrayMethodType);
        il.Emit(OpCodes.Ldfld, runtime.BoundArrayMethodNameField);
        il.Emit(OpCodes.Newobj, runtime.BoundArrayMethodCtor);
        il.Emit(OpCodes.Ldloc, callArgsLocal);
        il.Emit(OpCodes.Callvirt, runtime.BoundArrayMethodInvoke);
        il.Emit(OpCodes.Ret);

        if (_features.UsesMap)
        {
            il.MarkLabel(isBoundMapMethodLabel);
            il.Emit(OpCodes.Ldloc, thisArgLocal);
            il.Emit(OpCodes.Castclass, _types.DictionaryObjectObject);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, targetField);
            il.Emit(OpCodes.Castclass, runtime.BoundMapMethodType);
            il.Emit(OpCodes.Ldfld, runtime.BoundMapMethodNameField);
            il.Emit(OpCodes.Newobj, runtime.BoundMapMethodCtor);
            il.Emit(OpCodes.Ldloc, callArgsLocal);
            il.Emit(OpCodes.Callvirt, runtime.BoundMapMethodInvoke);
            il.Emit(OpCodes.Ret);
        }

        if (_features.UsesSet)
        {
            il.MarkLabel(isBoundSetMethodLabel);
            il.Emit(OpCodes.Ldloc, thisArgLocal);
            il.Emit(OpCodes.Castclass, _types.HashSetOfObject);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, targetField);
            il.Emit(OpCodes.Castclass, runtime.BoundSetMethodType);
            il.Emit(OpCodes.Ldfld, runtime.BoundSetMethodNameField);
            il.Emit(OpCodes.Newobj, runtime.BoundSetMethodCtor);
            il.Emit(OpCodes.Ldloc, callArgsLocal);
            il.Emit(OpCodes.Callvirt, runtime.BoundSetMethodInvoke);
            il.Emit(OpCodes.Ret);
        }

        // InvokeWithThis
        var iwtBuilder = typeBuilder.DefineMethod(
            "InvokeWithThis",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.ObjectArray]
        );
        var iwtIL = iwtBuilder.GetILGenerator();
        iwtIL.Emit(OpCodes.Ldarg_0);
        iwtIL.Emit(OpCodes.Ldarg_2);
        iwtIL.Emit(OpCodes.Callvirt, invokeBuilder);
        iwtIL.Emit(OpCodes.Ret);

        // Finalize the type
        typeBuilder.CreateType();
    }

    /// <summary>
    /// Emits the $FunctionApplyWrapper class.
    /// When invoked, calls the target function with thisArg and spread argsArray.
    /// </summary>
    private void EmitFunctionApplyWrapperClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$FunctionApplyWrapper",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.FunctionApplyWrapperType = typeBuilder;

        var targetField = typeBuilder.DefineField("_target", _types.Object, FieldAttributes.Private);

        // Constructor
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object]
        );
        runtime.FunctionApplyWrapperCtor = ctorBuilder;

        var ctorIL = ctorBuilder.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, targetField);
        ctorIL.Emit(OpCodes.Ret);

        // Invoke: object Invoke(object[] args)
        // args[0] = thisArg, args[1] = argsArray (can be null or array)
        var invokeBuilder = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [_types.ObjectArray]
        );
        runtime.FunctionApplyWrapperInvoke = invokeBuilder;

        var il = invokeBuilder.GetILGenerator();
        var thisArgLocal = il.DeclareLocal(_types.Object);
        var argsArrayLocal = il.DeclareLocal(_types.Object);
        var callArgsLocal = il.DeclareLocal(_types.ObjectArray);
        var argsLenLocal = il.DeclareLocal(_types.Int32);

        // argsLen = args?.Length ?? 0
        var noArgsLabel = il.DefineLabel();
        var afterArgsLenLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, noArgsLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, argsLenLocal);
        il.Emit(OpCodes.Br, afterArgsLenLabel);
        il.MarkLabel(noArgsLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, argsLenLocal);
        il.MarkLabel(afterArgsLenLabel);

        // thisArg = argsLen > 0 ? args[0] : null
        var noThisLabel = il.DefineLabel();
        var afterThisLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, noThisLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, thisArgLocal);
        il.Emit(OpCodes.Br, afterThisLabel);
        il.MarkLabel(noThisLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, thisArgLocal);
        il.MarkLabel(afterThisLabel);

        // argsArray = argsLen > 1 ? args[1] : null
        var noArgsArrayLabel = il.DefineLabel();
        var afterArgsArrayLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, argsLenLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ble, noArgsArrayLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, argsArrayLocal);
        il.Emit(OpCodes.Br, afterArgsArrayLabel);
        il.MarkLabel(noArgsArrayLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, argsArrayLocal);
        il.MarkLabel(afterArgsArrayLabel);

        // Convert argsArray to object[]
        // if (argsArray == null) callArgs = empty
        // else if (argsArray is List<object?>) callArgs = ToArray()
        // else if (argsArray is object[]) callArgs = argsArray
        // else callArgs = empty
        var isListLabel = il.DefineLabel();
        var isArrayLabel = il.DefineLabel();
        var isTSArrayLabel = il.DefineLabel();
        var afterConvertLabel = il.DefineLabel();
        var nullArgsArrayLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Brfalse, nullArgsArrayLabel);  // if argsArray is null, use empty array

        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Isinst, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Brtrue, isListLabel);

        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Isinst, _types.ObjectArray);
        il.Emit(OpCodes.Brtrue, isArrayLabel);

        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, isTSArrayLabel);

        // Unknown type - empty array
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(_types.GetMethod(typeof(Array), "Empty"), _types.Object));
        il.Emit(OpCodes.Stloc, callArgsLocal);
        il.Emit(OpCodes.Br, afterConvertLabel);

        il.MarkLabel(isListLabel);
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Castclass, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObjectNullable, "ToArray")!);
        il.Emit(OpCodes.Stloc, callArgsLocal);
        il.Emit(OpCodes.Br, afterConvertLabel);

        il.MarkLabel(isArrayLabel);
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Castclass, _types.ObjectArray);
        il.Emit(OpCodes.Stloc, callArgsLocal);
        il.Emit(OpCodes.Br, afterConvertLabel);

        il.MarkLabel(isTSArrayLabel);
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayElementsGetter);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObjectNullable, "ToArray")!);
        il.Emit(OpCodes.Stloc, callArgsLocal);
        il.Emit(OpCodes.Br, afterConvertLabel);

        // null argsArray - use empty array
        il.MarkLabel(nullArgsArrayLabel);
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(_types.GetMethod(typeof(Array), "Empty"), _types.Object));
        il.Emit(OpCodes.Stloc, callArgsLocal);

        il.MarkLabel(afterConvertLabel);

        // Check target type and invoke. TSFunction/BoundTSFunction honor thisArg;
        // bound methods ignore it (receiver already captured).
        var isTSFunctionLabel = il.DefineLabel();
        var isBoundTSFunctionLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, isTSFunctionLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, isBoundTSFunctionLabel);

        // Non-TSFunction callables, including proxies, retain thisArg.
        EmitDispatchToTarget(
            il, runtime, targetField, callArgsLocal, thisArgLocal);

        // Fall-through: unknown target type → return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(isTSFunctionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldloc, thisArgLocal);
        il.Emit(OpCodes.Ldloc, callArgsLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(isBoundTSFunctionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Ldloc, callArgsLocal);
        il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvoke);
        il.Emit(OpCodes.Ret);

        // InvokeWithThis
        var iwtBuilder = typeBuilder.DefineMethod(
            "InvokeWithThis",
            MethodAttributes.Public,
            _types.Object,
            [_types.Object, _types.ObjectArray]
        );
        var iwtIL = iwtBuilder.GetILGenerator();
        iwtIL.Emit(OpCodes.Ldarg_0);
        iwtIL.Emit(OpCodes.Ldarg_2);
        iwtIL.Emit(OpCodes.Callvirt, invokeBuilder);
        iwtIL.Emit(OpCodes.Ret);

        // Finalize the type
        typeBuilder.CreateType();
    }
}
