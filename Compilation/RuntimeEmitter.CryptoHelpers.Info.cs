using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

/// <summary>
/// Emits crypto.constants, getCipherInfo, and getCurves (#1056/#1057/#1058).
/// All values come from the shared <see cref="CryptoInfoTables"/> so interpreter
/// and compiled output agree. Pure-BCL IL (standalone).
/// </summary>
public partial class RuntimeEmitter
{
    /// <summary>Emits: public static object CryptoGetConstants() → $Object of crypto.constants.</summary>
    private void EmitCryptoGetConstants(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoGetConstants",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes);
        runtime.CryptoGetConstants = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Newobj, runtime.TSObjectCtor);
        var objLocal = il.DeclareLocal(runtime.TSObjectType);
        il.Emit(OpCodes.Stloc, objLocal);

        foreach (var (name, value) in CryptoInfoTables.NumericConstants)
        {
            il.Emit(OpCodes.Ldloc, objLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldc_R8, value);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Callvirt, runtime.TSObjectSetProperty);
        }
        foreach (var (name, value) in CryptoInfoTables.StringConstants)
        {
            il.Emit(OpCodes.Ldloc, objLocal);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldstr, value);
            il.Emit(OpCodes.Callvirt, runtime.TSObjectSetProperty);
        }

        il.Emit(OpCodes.Ldloc, objLocal);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CryptoGetCipherInfo(object nameOrNid, object options)
    /// → { name, nid, blockSize, ivLength, keyLength, mode } $Object, or undefined.
    /// </summary>
    private void EmitCryptoGetCipherInfo(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoGetCipherInfo",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]);
        runtime.CryptoGetCipherInfo = method;

        var il = method.GetILGenerator();

        // Determine lookup: string name (lowercased) or numeric nid.
        var nameLocal = il.DeclareLocal(_types.String);
        var nidLocal = il.DeclareLocal(_types.Int32);
        var isNameLocal = il.DeclareLocal(_types.Boolean);

        var numericLabel = il.DefineLabel();
        var afterKeyLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brfalse, numericLabel);
        // name = ((string)arg0).ToLowerInvariant(); isName = true
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("ToLowerInvariant")!);
        il.Emit(OpCodes.Stloc, nameLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, isNameLocal);
        il.Emit(OpCodes.Br, afterKeyLabel);

        il.MarkLabel(numericLabel);
        // nid = (int)(double)arg0 (if double); else return undefined
        var isDoubleLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, isDoubleLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(isDoubleLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, nidLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, isNameLocal);

        il.MarkLabel(afterKeyLabel);

        foreach (var info in CryptoInfoTables.CipherInfos)
        {
            var nextLabel = il.DefineLabel();
            var matchLabel = il.DefineLabel();

            // if (isName) match on name; else match on nid
            var checkNidLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, isNameLocal);
            il.Emit(OpCodes.Brfalse, checkNidLabel);
            il.Emit(OpCodes.Ldloc, nameLocal);
            il.Emit(OpCodes.Ldstr, info.Name);
            il.Emit(OpCodes.Call, _types.String.GetMethod("op_Equality", [_types.String, _types.String])!);
            il.Emit(OpCodes.Brtrue, matchLabel);
            il.Emit(OpCodes.Br, nextLabel);
            il.MarkLabel(checkNidLabel);
            il.Emit(OpCodes.Ldloc, nidLocal);
            il.Emit(OpCodes.Ldc_I4, info.Nid);
            il.Emit(OpCodes.Bne_Un, nextLabel);

            il.MarkLabel(matchLabel);

            // Test options: keyLength/ivLength mismatch → undefined
            EmitCipherInfoOptionCheck(il, runtime, "keyLength", info.KeyLength);
            EmitCipherInfoOptionCheck(il, runtime, "ivLength", info.IvLength);

            // Build result object
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.DictionaryStringObject));
            il.Emit(OpCodes.Newobj, runtime.TSObjectCtor);
            var resLocal = il.DeclareLocal(runtime.TSObjectType);
            il.Emit(OpCodes.Stloc, resLocal);

            void SetStr(string k, string v)
            {
                il.Emit(OpCodes.Ldloc, resLocal);
                il.Emit(OpCodes.Ldstr, k);
                il.Emit(OpCodes.Ldstr, v);
                il.Emit(OpCodes.Callvirt, runtime.TSObjectSetProperty);
            }
            void SetNum(string k, int v)
            {
                il.Emit(OpCodes.Ldloc, resLocal);
                il.Emit(OpCodes.Ldstr, k);
                il.Emit(OpCodes.Ldc_R8, (double)v);
                il.Emit(OpCodes.Box, _types.Double);
                il.Emit(OpCodes.Callvirt, runtime.TSObjectSetProperty);
            }
            SetStr("name", info.Name);
            SetNum("nid", info.Nid);
            SetNum("blockSize", info.BlockSize);
            SetNum("ivLength", info.IvLength);
            SetNum("keyLength", info.KeyLength);
            SetStr("mode", info.Mode);

            il.Emit(OpCodes.Ldloc, resLocal);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(nextLabel);
        }

        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits (inline): if options[key] is a double != expected, return undefined.
    /// </summary>
    private void EmitCipherInfoOptionCheck(ILGenerator il, EmittedRuntime runtime, string key, int expected)
    {
        var skipLabel = il.DefineLabel();
        var valLocal = il.DeclareLocal(_types.Object);

        // if (options is not $Object) skip
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, skipLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldstr, key);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        il.Emit(OpCodes.Stloc, valLocal);

        // if (val is not double) skip
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, skipLabel);

        // if ((int)(double)val == expected) skip else return undefined
        il.Emit(OpCodes.Ldloc, valLocal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4, expected);
        il.Emit(OpCodes.Beq, skipLabel);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(skipLabel);
    }

    /// <summary>Emits: public static object CryptoGetCurves() → $Array of curve names.</summary>
    private void EmitCryptoGetCurves(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CryptoGetCurves",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            Type.EmptyTypes);
        runtime.CryptoGetCurves = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        il.Emit(OpCodes.Stloc, listLocal);

        foreach (var curve in CryptoInfoTables.SupportedCurves)
        {
            il.Emit(OpCodes.Ldloc, listLocal);
            il.Emit(OpCodes.Ldstr, curve);
            il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add", [_types.Object])!);
        }

        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor);
        il.Emit(OpCodes.Ret);
    }
}
