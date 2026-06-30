using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitReferenceEqualityComparerClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $ReferenceEqualityComparer : IEqualityComparer<object>
        // This implements JavaScript-style equality for Map/Set keys:
        // - Primitives (string, double, bool): value equality
        // - Objects: reference equality
        var typeBuilder = moduleBuilder.DefineType(
            "$ReferenceEqualityComparer",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object,
            [_types.IEqualityComparerOfObject]
        );
        _ = typeBuilder;

        // Static Instance field (singleton)
        var instanceField = typeBuilder.DefineField(
            "Instance",
            typeBuilder,
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly
        );
        runtime.ReferenceEqualityComparerInstance = instanceField;

        // Private constructor
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Private,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var ctorIL = ctorBuilder.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ret);

        // Static constructor to initialize Instance
        var cctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var cctorIL = cctorBuilder.GetILGenerator();
        cctorIL.Emit(OpCodes.Newobj, ctorBuilder);
        cctorIL.Emit(OpCodes.Stsfld, instanceField);
        cctorIL.Emit(OpCodes.Ret);

        // Equals method: public bool Equals(object? x, object? y)
        EmitReferenceEqualityComparerEquals(typeBuilder, runtime);

        // GetHashCode method: public int GetHashCode(object obj)
        EmitReferenceEqualityComparerGetHashCode(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    private void EmitReferenceEqualityComparerEquals(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "Equals",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final,
            _types.Boolean,
            [_types.Object, _types.Object]
        );

        var il = method.GetILGenerator();

        // Labels for control flow
        var returnTrueLabel = il.DefineLabel();
        var returnFalseLabel = il.DefineLabel();
        var checkPrimitivesLabel = il.DefineLabel();
        var checkBigIntLabel = il.DefineLabel();
        var checkSymbolLabel = il.DefineLabel();
        var useReferenceEqualityLabel = il.DefineLabel();
        var useValueEqualityLabel = il.DefineLabel();

        // if (x is null && y is null) return true;
        il.Emit(OpCodes.Ldarg_1);  // x
        il.Emit(OpCodes.Brtrue, checkPrimitivesLabel);  // x not null, check primitives

        // x is null - check if y is also null
        il.Emit(OpCodes.Ldarg_2);  // y
        il.Emit(OpCodes.Brfalse, returnTrueLabel);  // both null, return true

        // x is null but y is not null
        il.Emit(OpCodes.Br, returnFalseLabel);

        // x is not null - check if y is null
        il.MarkLabel(checkPrimitivesLabel);
        il.Emit(OpCodes.Ldarg_2);  // y
        il.Emit(OpCodes.Brfalse, returnFalseLabel);  // x not null but y is null

        // Both are not null - check primitives
        // if (x is string || x is double || x is bool) return object.Equals(x, y);
        il.Emit(OpCodes.Ldarg_1);  // x
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, useValueEqualityLabel);

        il.Emit(OpCodes.Ldarg_1);  // x
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, useValueEqualityLabel);

        il.Emit(OpCodes.Ldarg_1);  // x
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, useValueEqualityLabel);

        // Check for BigInteger value equality
        il.MarkLabel(checkBigIntLabel);
        il.Emit(OpCodes.Ldarg_1);  // x
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brfalse, checkSymbolLabel);

        // x is BigInteger - check if y is also BigInteger
        il.Emit(OpCodes.Ldarg_2);  // y
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brfalse, returnFalseLabel);

        // Both are BigInteger - compare values
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.BigInteger);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Unbox_Any, _types.BigInteger);
        il.Emit(OpCodes.Call, _types.BigInteger.GetMethod("op_Equality", [_types.BigInteger, _types.BigInteger])!);
        il.Emit(OpCodes.Ret);

        // Check for Symbol - use reference equality
        il.MarkLabel(checkSymbolLabel);
        il.Emit(OpCodes.Ldarg_1);  // x
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, useReferenceEqualityLabel);

        il.Emit(OpCodes.Ldarg_2);  // y
        il.Emit(OpCodes.Isinst, runtime.TSSymbolType);
        il.Emit(OpCodes.Brtrue, useReferenceEqualityLabel);

        // Default: use reference equality for all other objects
        il.MarkLabel(useReferenceEqualityLabel);
        il.Emit(OpCodes.Ldarg_1);  // x
        il.Emit(OpCodes.Ldarg_2);  // y
        il.Emit(OpCodes.Ceq);  // Reference equality check
        il.Emit(OpCodes.Ret);

        // Use value equality (Object.Equals) - for primitives
        il.MarkLabel(useValueEqualityLabel);
        il.Emit(OpCodes.Ldarg_1);  // x
        il.Emit(OpCodes.Ldarg_2);  // y
        il.Emit(OpCodes.Call, _types.Object.GetMethod("Equals", [_types.Object, _types.Object])!);
        il.Emit(OpCodes.Ret);

        // Return true
        il.MarkLabel(returnTrueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // Return false
        il.MarkLabel(returnFalseLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    private void EmitReferenceEqualityComparerGetHashCode(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetHashCode",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final,
            _types.Int32,
            [_types.Object]
        );

        var il = method.GetILGenerator();

        // Labels
        var notNullLabel = il.DefineLabel();
        var notStringLabel = il.DefineLabel();
        var notDoubleLabel = il.DefineLabel();
        var notBoolLabel = il.DefineLabel();
        var notBigIntLabel = il.DefineLabel();
        var defaultLabel = il.DefineLabel();

        // if (obj == null) return 0;
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brtrue, notNullLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNullLabel);

        // if (obj is string s) return s.GetHashCode();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, notStringLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.String, "GetHashCode"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notStringLabel);

        // if (obj is double d) return d.GetHashCode();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notDoubleLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        var doubleLocal = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Stloc, doubleLocal);
        il.Emit(OpCodes.Ldloca, doubleLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Double, "GetHashCode"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notDoubleLabel);

        // if (obj is bool b) return b.GetHashCode();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, notBoolLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        var boolLocal = il.DeclareLocal(_types.Boolean);
        il.Emit(OpCodes.Stloc, boolLocal);
        il.Emit(OpCodes.Ldloca, boolLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Boolean, "GetHashCode"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notBoolLabel);

        // if (obj is BigInteger bigInt) return bigInt.GetHashCode();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brfalse, notBigIntLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.BigInteger);
        var bigIntLocal = il.DeclareLocal(_types.BigInteger);
        il.Emit(OpCodes.Stloc, bigIntLocal);
        il.Emit(OpCodes.Ldloca, bigIntLocal);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.BigInteger, "GetHashCode"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notBigIntLabel);

        // default: return RuntimeHelpers.GetHashCode(obj);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.RuntimeHelpers, "GetHashCode", [_types.Object]));
        il.Emit(OpCodes.Ret);
    }
}
