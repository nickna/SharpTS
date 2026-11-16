using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitWithEnvironmentHelpers(TypeBuilder builder, EmittedRuntime runtime)
    {
        var toObject = builder.DefineMethod("WithObject", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object]);
        runtime.ObjectConstruction.WithObject = toObject;
        var il = toObject.GetILGenerator();
        var invalid = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, invalid);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.Sentinels.UndefinedType);
        il.Emit(OpCodes.Brtrue, invalid);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.BoxedPrimitives.ToObject);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(invalid);
        GuestErrorEmitter.ThrowError(il, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor,
            "Cannot convert null or undefined to object");

        var hasBinding = builder.DefineMethod("WithHasBinding", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.String]);
        runtime.ObjectConstruction.WithHasBinding = hasBinding;
        il = hasBinding.GetILGenerator();
        var absent = il.DefineLabel();
        var present = il.DefineLabel();
        var inspect = il.DefineLabel();
        var unscopables = il.DeclareLocal(_types.Object);
        var kind = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Operators.HasIn);
        il.Emit(OpCodes.Brfalse, absent);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.Symbols.Unscopables);
        il.Emit(OpCodes.Call, runtime.ObjectRead.Index);
        il.Emit(OpCodes.Stloc, unscopables);
        il.Emit(OpCodes.Ldloc, unscopables);
        il.Emit(OpCodes.Brfalse, present);
        il.Emit(OpCodes.Ldloc, unscopables);
        il.Emit(OpCodes.Call, runtime.Operators.TypeOf);
        il.Emit(OpCodes.Stloc, kind);
        il.Emit(OpCodes.Ldloc, kind);
        il.Emit(OpCodes.Ldstr, "object");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brtrue, inspect);
        il.Emit(OpCodes.Ldloc, kind);
        il.Emit(OpCodes.Ldstr, "function");
        il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
        il.Emit(OpCodes.Brfalse, present);
        il.MarkLabel(inspect);
        il.Emit(OpCodes.Ldloc, unscopables);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ObjectRead.Property);
        il.Emit(OpCodes.Call, runtime.Booleans.IsTruthy);
        il.Emit(OpCodes.Brtrue, absent);
        il.MarkLabel(present);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(absent);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        var getCheck = builder.DefineMethod("WithGetCheck", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.String, _types.Object]);
        runtime.ObjectConstruction.WithGetCheck = getCheck;
        il = getCheck.GetILGenerator();
        var exists = il.DeclareLocal(_types.Boolean);
        var returnExists = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Operators.HasIn);
        il.Emit(OpCodes.Stloc, exists);
        il.Emit(OpCodes.Ldloc, exists);
        il.Emit(OpCodes.Brtrue, returnExists);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.Booleans.IsTruthy);
        il.Emit(OpCodes.Brfalse, returnExists);
        GuestErrorEmitter.ThrowError(il, runtime.Errors.CreateException, runtime.Errors.ReferenceErrorConstructor,
            "Binding no longer exists in with object");
        il.MarkLabel(returnExists);
        il.Emit(OpCodes.Ldloc, exists);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        var set = builder.DefineMethod("WithSet", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.String, _types.Object, _types.Object]);
        runtime.ObjectConstruction.WithSet = set;
        il = set.GetILGenerator();
        var write = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.Operators.HasIn);
        il.Emit(OpCodes.Brtrue, write);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, runtime.Booleans.IsTruthy);
        il.Emit(OpCodes.Brfalse, write);
        GuestErrorEmitter.ThrowError(il, runtime.Errors.CreateException, runtime.Errors.ReferenceErrorConstructor,
            "Binding no longer exists in with object");
        il.MarkLabel(write);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Call, runtime.Booleans.IsTruthy);
        il.Emit(OpCodes.Call, runtime.ObjectWrite.PropertyStrict);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ret);

        // A closed static delegate stores only the chosen receiver and callable.
        // Unlike Function.prototype.bind, capture performs no guest-visible
        // prototype/name/length reads and defers callability errors until invocation.
        var invoke = builder.DefineMethod("InvokeWithCapturedReference", MethodAttributes.Private | MethodAttributes.Static,
            _types.Object, [_types.ObjectArray, _types.ObjectArray]);
        il = invoke.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.Invocation.Method);
        il.Emit(OpCodes.Ret);

        var capture = builder.DefineMethod("WithCaptureCall", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.String, _types.Object]);
        runtime.ObjectConstruction.WithCaptureCall = capture;
        il = capture.GetILGenerator();
        var missing = il.DefineLabel();
        var returnCallee = il.DefineLabel();
        var callee = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, getCheck);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Brfalse, missing);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ObjectRead.Property);
        il.Emit(OpCodes.Stloc, callee);
        // Optional calls must still see nullish callees and skip their arguments.
        il.Emit(OpCodes.Ldloc, callee);
        il.Emit(OpCodes.Brfalse, returnCallee);
        il.Emit(OpCodes.Ldloc, callee);
        il.Emit(OpCodes.Isinst, runtime.Sentinels.UndefinedType);
        il.Emit(OpCodes.Brtrue, returnCallee);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldloc, callee);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Ldftn, invoke);
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.FuncObjectArrayToObject, _types.Object, _types.IntPtr));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(missing);
        il.Emit(OpCodes.Ldsfld, runtime.Sentinels.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(returnCallee);
        il.Emit(OpCodes.Ldloc, callee);
        il.Emit(OpCodes.Ret);
    }
}
