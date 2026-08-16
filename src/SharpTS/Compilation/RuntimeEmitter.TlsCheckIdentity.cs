using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// Emits tls.checkServerIdentity(host, cert) as pure-BCL IL on the $Runtime class.
/// Mirrors interp TlsModuleInterpreter.CheckIdentityCore / HostMatches / ExtractCN exactly,
/// so interp == compiled. Pure string logic — fully standalone.
/// </summary>
public partial class RuntimeEmitter
{
    private MethodBuilder _tlsCheckIdentityCoreMethod = null!;
    private MethodBuilder _tlsHostMatchesMethod = null!;
    private MethodBuilder _tlsExtractCnMethod = null!;

    private const int OrdinalCmp = (int)StringComparison.Ordinal;
    private const int OrdinalIgnoreCaseCmp = (int)StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Emits: public static object TlsCheckServerIdentity(object host, object cert)
    /// + the private static helper methods. Registers tls.checkServerIdentity.
    /// </summary>
    private void EmitTlsCheckServerIdentity(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitTlsHostMatchesHelper(typeBuilder);
        EmitTlsExtractCnHelper(typeBuilder);
        EmitTlsCheckIdentityCoreHelper(typeBuilder);

        var method = typeBuilder.DefineMethod(
            "TlsCheckServerIdentity",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        runtime.RegisterBuiltInModuleMethod("tls", "checkServerIdentity", method);

        var il = method.GetILGenerator();
        var dictType = _types.DictionaryStringObject;
        var tryGet = _types.GetMethod(dictType, "TryGetValue")!;

        // string h = host as string ?? "";
        var hLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Dup);
        var hOk = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, hOk);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldstr, "");
        il.MarkLabel(hOk);
        il.Emit(OpCodes.Stloc, hLocal);

        // string san = null, subj = null;
        var sanLocal = il.DeclareLocal(_types.String);
        var subjLocal = il.DeclareLocal(_types.String);
        var tmpLocal = il.DeclareLocal(_types.Object);

        // if (cert is Dictionary<string,object?> d)
        var notDict = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Brfalse, notDict);
        var dLocal = il.DeclareLocal(dictType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Stloc, dLocal);

        // san = d["subjectaltname"] as string
        var noSan = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldstr, "subjectaltname");
        il.Emit(OpCodes.Ldloca, tmpLocal);
        il.Emit(OpCodes.Callvirt, tryGet);
        il.Emit(OpCodes.Brfalse, noSan);
        il.Emit(OpCodes.Ldloc, tmpLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, sanLocal);
        il.MarkLabel(noSan);

        // subj: d["subject"] — string directly, or dict with "CN"
        var noSubj = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dLocal);
        il.Emit(OpCodes.Ldstr, "subject");
        il.Emit(OpCodes.Ldloca, tmpLocal);
        il.Emit(OpCodes.Callvirt, tryGet);
        il.Emit(OpCodes.Brfalse, noSubj);
        // if (tmp is string) subj = tmp
        var subjNotString = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tmpLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, subjNotString);
        il.Emit(OpCodes.Stloc, subjLocal);
        il.Emit(OpCodes.Br, noSubj);
        il.MarkLabel(subjNotString);
        il.Emit(OpCodes.Pop);
        // else if (tmp is dict sd && sd.TryGetValue("CN", out v) && v is string) subj = v
        var cnLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, tmpLocal);
        il.Emit(OpCodes.Isinst, dictType);
        il.Emit(OpCodes.Brfalse, noSubj);
        il.Emit(OpCodes.Ldloc, tmpLocal);
        il.Emit(OpCodes.Castclass, dictType);
        il.Emit(OpCodes.Ldstr, "CN");
        il.Emit(OpCodes.Ldloca, cnLocal);
        il.Emit(OpCodes.Callvirt, tryGet);
        il.Emit(OpCodes.Brfalse, noSubj);
        il.Emit(OpCodes.Ldloc, cnLocal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Stloc, subjLocal);
        il.MarkLabel(noSubj);

        il.MarkLabel(notDict);

        // string err = TlsCheckIdentityCore(h, san, subj);
        var errLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, hLocal);
        il.Emit(OpCodes.Ldloc, sanLocal);
        il.Emit(OpCodes.Ldloc, subjLocal);
        il.Emit(OpCodes.Call, _tlsCheckIdentityCoreMethod);
        il.Emit(OpCodes.Stloc, errLocal);

        // if (err == null) return $Undefined._instance;
        var hasErr = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, errLocal);
        il.Emit(OpCodes.Brtrue, hasErr);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        // return new $Error(err);
        il.MarkLabel(hasErr);
        il.Emit(OpCodes.Ldloc, errLocal);
        il.Emit(OpCodes.Newobj, runtime.TSErrorCtorMessage);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>private static bool TlsHostMatches(string host, string name)</summary>
    private void EmitTlsHostMatchesHelper(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "TlsHostMatches",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Boolean,
            [_types.String, _types.String]
        );
        _tlsHostMatchesMethod = method;
        var il = method.GetILGenerator();

        var startsWith = _types.GetMethod(_types.String, "StartsWith", [_types.String, typeof(StringComparison)])!;
        var indexOfChar = _types.GetMethod(_types.String, "IndexOf", [_types.Char])!;
        var substringI = _types.GetMethod(_types.String, "Substring", [_types.Int32])!;
        var equalsCmp = _types.GetMethod(_types.String, "Equals", [_types.String, _types.String, typeof(StringComparison)])!;

        // if (name.StartsWith("*.", Ordinal))
        var notWild = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "*.");
        il.Emit(OpCodes.Ldc_I4, OrdinalCmp);
        il.Emit(OpCodes.Callvirt, startsWith);
        il.Emit(OpCodes.Brfalse, notWild);

        // int dot = host.IndexOf('.');
        var dotLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)'.');
        il.Emit(OpCodes.Callvirt, indexOfChar);
        il.Emit(OpCodes.Stloc, dotLocal);
        // if (dot <= 0) return false
        var retFalse = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dotLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ble, retFalse);
        // return string.Equals(host.Substring(dot), name.Substring(1), OrdinalIgnoreCase)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, dotLocal);
        il.Emit(OpCodes.Callvirt, substringI);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, substringI);
        il.Emit(OpCodes.Ldc_I4, OrdinalIgnoreCaseCmp);
        il.Emit(OpCodes.Call, equalsCmp);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(retFalse);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        // return string.Equals(host, name, OrdinalIgnoreCase)
        il.MarkLabel(notWild);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, OrdinalIgnoreCaseCmp);
        il.Emit(OpCodes.Call, equalsCmp);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>private static string TlsExtractCN(string dn)</summary>
    private void EmitTlsExtractCnHelper(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "TlsExtractCN",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [_types.String]
        );
        _tlsExtractCnMethod = method;
        var il = method.GetILGenerator();

        var indexOfStrCmp = _types.GetMethod(_types.String, "IndexOf", [_types.String, typeof(StringComparison)])!;
        var indexOfCharFrom = _types.GetMethod(_types.String, "IndexOf", [_types.Char, _types.Int32])!;
        var substringI = _types.GetMethod(_types.String, "Substring", [_types.Int32])!;
        var substringII = _types.GetMethod(_types.String, "Substring", [_types.Int32, _types.Int32])!;
        var trim = _types.GetMethod(_types.String, "Trim", Type.EmptyTypes)!;

        // int i = dn.IndexOf("CN=", OrdinalIgnoreCase)
        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "CN=");
        il.Emit(OpCodes.Ldc_I4, OrdinalIgnoreCaseCmp);
        il.Emit(OpCodes.Callvirt, indexOfStrCmp);
        il.Emit(OpCodes.Stloc, iLocal);

        // if (i < 0) return dn
        var hasCN = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, hasCN);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(hasCN);
        // start = i + 3
        var startLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_3);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, startLocal);
        // end = dn.IndexOf(',', start)
        var endLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)',');
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Callvirt, indexOfCharFrom);
        il.Emit(OpCodes.Stloc, endLocal);

        // if (end < 0) return dn.Substring(start).Trim()
        var hasEnd = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Bge, hasEnd);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Callvirt, substringI);
        il.Emit(OpCodes.Callvirt, trim);
        il.Emit(OpCodes.Ret);

        // return dn.Substring(start, end - start).Trim()
        il.MarkLabel(hasEnd);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Ldloc, endLocal);
        il.Emit(OpCodes.Ldloc, startLocal);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt, substringII);
        il.Emit(OpCodes.Callvirt, trim);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>private static string TlsCheckIdentityCore(string host, string san, string subjDN)</summary>
    private void EmitTlsCheckIdentityCoreHelper(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "TlsCheckIdentityCore",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.String, _types.String]
        );
        _tlsCheckIdentityCoreMethod = method;
        var il = method.GetILGenerator();

        var listType = typeof(List<string>);
        var listAdd = listType.GetMethod("Add")!;
        var listCount = listType.GetProperty("Count")!.GetGetMethod()!;
        var listGetItem = listType.GetMethod("get_Item", [_types.Int32])!;
        var isNullOrEmpty = _types.GetMethod(_types.String, "IsNullOrEmpty", [_types.String])!;
        var splitChar = _types.GetMethod(_types.String, "Split", [_types.Char, typeof(StringSplitOptions)])!;
        var trim = _types.GetMethod(_types.String, "Trim", Type.EmptyTypes)!;
        var startsWith = _types.GetMethod(_types.String, "StartsWith", [_types.String, typeof(StringComparison)])!;
        var substringI = _types.GetMethod(_types.String, "Substring", [_types.Int32])!;
        var joinEnum = _types.GetMethod(_types.String, "Join", [_types.String, typeof(IEnumerable<string>)])!;
        var concat4 = _types.GetMethod(_types.String, "Concat", [_types.String, _types.String, _types.String, _types.String])!;

        var namesLocal = il.DeclareLocal(listType);
        il.Emit(OpCodes.Newobj, listType.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, namesLocal);

        // if (!IsNullOrEmpty(san)) { foreach part in san.Split(',') ... }
        var sanDone = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, isNullOrEmpty);
        il.Emit(OpCodes.Brtrue, sanDone);

        var partsLocal = il.DeclareLocal(typeof(string[]));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, (int)',');
        il.Emit(OpCodes.Ldc_I4_0); // StringSplitOptions.None
        il.Emit(OpCodes.Callvirt, splitChar);
        il.Emit(OpCodes.Stloc, partsLocal);

        var iLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var loopTop = il.DefineLabel();
        var loopChk = il.DefineLabel();
        il.Emit(OpCodes.Br, loopChk);

        il.MarkLabel(loopTop);
        // string p = parts[i].Trim()
        var pLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Callvirt, trim);
        il.Emit(OpCodes.Stloc, pLocal);
        // if (p.StartsWith("DNS:", Ordinal)) names.Add(p.Substring(4))
        var notDns = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, pLocal);
        il.Emit(OpCodes.Ldstr, "DNS:");
        il.Emit(OpCodes.Ldc_I4, OrdinalCmp);
        il.Emit(OpCodes.Callvirt, startsWith);
        il.Emit(OpCodes.Brfalse, notDns);
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Ldloc, pLocal);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Callvirt, substringI);
        il.Emit(OpCodes.Callvirt, listAdd);
        var partNext = il.DefineLabel();
        il.Emit(OpCodes.Br, partNext);
        il.MarkLabel(notDns);
        // else if (p.StartsWith("IP Address:", Ordinal)) names.Add(p.Substring(11))
        var notIp = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, pLocal);
        il.Emit(OpCodes.Ldstr, "IP Address:");
        il.Emit(OpCodes.Ldc_I4, OrdinalCmp);
        il.Emit(OpCodes.Callvirt, startsWith);
        il.Emit(OpCodes.Brfalse, notIp);
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Ldloc, pLocal);
        il.Emit(OpCodes.Ldc_I4, 11);
        il.Emit(OpCodes.Callvirt, substringI);
        il.Emit(OpCodes.Callvirt, listAdd);
        il.MarkLabel(notIp);
        il.MarkLabel(partNext);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.MarkLabel(loopChk);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, partsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Blt, loopTop);

        il.MarkLabel(sanDone);

        // if (names.Count == 0 && !IsNullOrEmpty(subjDN)) { cn = TlsExtractCN(subjDN); if (!empty) names.Add(cn) }
        var cnDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Callvirt, listCount);
        il.Emit(OpCodes.Brtrue, cnDone);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, isNullOrEmpty);
        il.Emit(OpCodes.Brtrue, cnDone);
        var cnLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, _tlsExtractCnMethod);
        il.Emit(OpCodes.Stloc, cnLocal);
        il.Emit(OpCodes.Ldloc, cnLocal);
        il.Emit(OpCodes.Call, isNullOrEmpty);
        il.Emit(OpCodes.Brtrue, cnDone);
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Ldloc, cnLocal);
        il.Emit(OpCodes.Callvirt, listAdd);
        il.MarkLabel(cnDone);

        // foreach name in names: if (TlsHostMatches(host, name)) return null;
        var jLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, jLocal);
        var mTop = il.DefineLabel();
        var mChk = il.DefineLabel();
        il.Emit(OpCodes.Br, mChk);
        il.MarkLabel(mTop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Callvirt, listGetItem);
        il.Emit(OpCodes.Call, _tlsHostMatchesMethod);
        var noMatch = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, noMatch);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(noMatch);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, jLocal);
        il.MarkLabel(mChk);
        il.Emit(OpCodes.Ldloc, jLocal);
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Callvirt, listCount);
        il.Emit(OpCodes.Blt, mTop);

        // altText = names.Count>0 ? string.Join(", ", names) : "<no cert names>"
        var altLocal = il.DeclareLocal(_types.String);
        var emptyNames = il.DefineLabel();
        var altDone = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Callvirt, listCount);
        il.Emit(OpCodes.Brfalse, emptyNames);
        il.Emit(OpCodes.Ldstr, ", ");
        il.Emit(OpCodes.Ldloc, namesLocal);
        il.Emit(OpCodes.Call, joinEnum);
        il.Emit(OpCodes.Stloc, altLocal);
        il.Emit(OpCodes.Br, altDone);
        il.MarkLabel(emptyNames);
        il.Emit(OpCodes.Ldstr, "<no cert names>");
        il.Emit(OpCodes.Stloc, altLocal);
        il.MarkLabel(altDone);

        // return string.Concat("Host: ", host, ". is not in the cert's altnames: ", altText)
        il.Emit(OpCodes.Ldstr, "Host: ");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, ". is not in the cert's altnames: ");
        il.Emit(OpCodes.Ldloc, altLocal);
        il.Emit(OpCodes.Call, concat4);
        il.Emit(OpCodes.Ret);
    }
}
