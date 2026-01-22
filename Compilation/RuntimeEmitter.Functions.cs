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
        var typeBuilder = moduleBuilder.DefineType(
            "$BoundTSFunction",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.BoundTSFunctionType = typeBuilder;

        // Fields
        var targetField = typeBuilder.DefineField("_target", runtime.TSFunctionType, FieldAttributes.Private);
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
        invokeIL.Emit(OpCodes.Call, _types.ArrayType.GetMethod("Copy", [_types.ArrayType, _types.Int32, _types.ArrayType, _types.Int32, _types.Int32])!);
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
        invokeIL.Emit(OpCodes.Call, _types.ArrayType.GetMethod("Copy", [_types.ArrayType, _types.Int32, _types.ArrayType, _types.Int32, _types.Int32])!);
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
    /// Emits the GetFunctionMethod helper that returns bind/call/apply wrappers.
    /// </summary>
    private void EmitGetFunctionMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // GetFunctionMethod(object func, string methodName) -> object or null
        var method = typeBuilder.DefineMethod(
            "GetFunctionMethod",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.String]
        );
        runtime.GetFunctionMethod = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var bindLabel = il.DefineLabel();
        var callLabel = il.DefineLabel();
        var applyLabel = il.DefineLabel();
        var lengthLabel = il.DefineLabel();
        var nameLabel = il.DefineLabel();

        // if (func == null) return null
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

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

        // Unknown method - return null
        il.Emit(OpCodes.Br, nullLabel);

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

        // length: return 0.0 (arity - simplified)
        il.MarkLabel(lengthLabel);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        // name: return ""
        il.MarkLabel(nameLabel);
        il.Emit(OpCodes.Ldstr, "");
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the $FunctionBindWrapper class.
    /// When invoked, creates a $BoundTSFunction.
    /// </summary>
    private void EmitFunctionBindWrapperClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
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
        il.Emit(OpCodes.Call, _types.ArrayType.GetMethod("Copy", [_types.ArrayType, _types.Int32, _types.ArrayType, _types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Br, afterBoundArgsLabel);

        il.MarkLabel(noBoundArgsLabel);
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(Array), "Empty").MakeGenericMethod(_types.Object));
        il.Emit(OpCodes.Stloc, boundArgsLocal);
        il.MarkLabel(afterBoundArgsLabel);

        // Check if target is $TSFunction or $BoundTSFunction
        var isTSFunctionLabel = il.DefineLabel();
        var isBoundLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, isTSFunctionLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, isBoundLabel);

        // Unknown target type - return null
        il.Emit(OpCodes.Ldnull);
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

        // For already bound functions, we can't re-bind (simplified)
        il.MarkLabel(isBoundLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
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
        var typeBuilder = moduleBuilder.DefineType(
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
        il.Emit(OpCodes.Call, _types.ArrayType.GetMethod("Copy", [_types.ArrayType, _types.Int32, _types.ArrayType, _types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Br, afterCallArgsLabel);

        il.MarkLabel(noCallArgsLabel);
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(Array), "Empty").MakeGenericMethod(_types.Object));
        il.Emit(OpCodes.Stloc, callArgsLocal);
        il.MarkLabel(afterCallArgsLabel);

        // Check target type and invoke
        var isTSFunctionLabel = il.DefineLabel();
        var isBoundLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, isTSFunctionLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, isBoundLabel);

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
        il.MarkLabel(isBoundLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Castclass, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Ldloc, thisArgLocal);
        il.Emit(OpCodes.Ldloc, callArgsLocal);
        il.Emit(OpCodes.Callvirt, runtime.BoundTSFunctionInvokeWithThis);
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

    /// <summary>
    /// Emits the $FunctionApplyWrapper class.
    /// When invoked, calls the target function with thisArg and spread argsArray.
    /// </summary>
    private void EmitFunctionApplyWrapperClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
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
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(Array), "Empty").MakeGenericMethod(_types.Object));
        il.Emit(OpCodes.Stloc, callArgsLocal);
        il.Emit(OpCodes.Br, afterConvertLabel);

        il.MarkLabel(isListLabel);
        il.Emit(OpCodes.Ldloc, argsArrayLocal);
        il.Emit(OpCodes.Castclass, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetMethod("ToArray")!);
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
        il.Emit(OpCodes.Callvirt, _types.ListOfObjectNullable.GetMethod("ToArray")!);
        il.Emit(OpCodes.Stloc, callArgsLocal);
        il.Emit(OpCodes.Br, afterConvertLabel);

        // null argsArray - use empty array
        il.MarkLabel(nullArgsArrayLabel);
        il.Emit(OpCodes.Call, _types.GetMethod(typeof(Array), "Empty").MakeGenericMethod(_types.Object));
        il.Emit(OpCodes.Stloc, callArgsLocal);

        il.MarkLabel(afterConvertLabel);

        // Check target type and invoke
        var isTSFunctionLabel = il.DefineLabel();
        var isBoundLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, isTSFunctionLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, targetField);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, isBoundLabel);

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

        il.MarkLabel(isBoundLabel);
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
