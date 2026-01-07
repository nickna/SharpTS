using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

/// <summary>
/// RegExp-related runtime emission methods.
/// </summary>
public partial class RuntimeEmitter
{
    private static void EmitRegExpMethods(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitCreateRegExp(typeBuilder, runtime);
        EmitCreateRegExpWithFlags(typeBuilder, runtime);
        EmitRegExpTest(typeBuilder, runtime);
        EmitRegExpExec(typeBuilder, runtime);
        EmitRegExpToString(typeBuilder, runtime);
        EmitRegExpGetSource(typeBuilder, runtime);
        EmitRegExpGetFlags(typeBuilder, runtime);
        EmitRegExpGetGlobal(typeBuilder, runtime);
        EmitRegExpGetIgnoreCase(typeBuilder, runtime);
        EmitRegExpGetMultiline(typeBuilder, runtime);
        EmitRegExpGetLastIndex(typeBuilder, runtime);
        EmitRegExpSetLastIndex(typeBuilder, runtime);
        EmitStringMatchRegExp(typeBuilder, runtime);
        EmitStringReplaceRegExp(typeBuilder, runtime);
        EmitStringSearchRegExp(typeBuilder, runtime);
        EmitStringSplitRegExp(typeBuilder, runtime);
    }

    private static void EmitCreateRegExp(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateRegExp",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String]
        );
        runtime.CreateRegExp = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RegExpRuntimeHelpers).GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitCreateRegExpWithFlags(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CreateRegExpWithFlags",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.String]
        );
        runtime.CreateRegExpWithFlags = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RegExpRuntimeHelpers).GetMethod("CreateWithFlags", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitRegExpTest(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpTest",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object, _types.String]
        );
        runtime.RegExpTest = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RegExpRuntimeHelpers).GetMethod("Test", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitRegExpExec(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpExec",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.String]
        );
        runtime.RegExpExec = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RegExpRuntimeHelpers).GetMethod("Exec", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitRegExpToString(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpToString",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.RegExpToString = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RegExpRuntimeHelpers).GetMethod("ToString", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitRegExpGetSource(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpGetSource",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.RegExpGetSource = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RegExpRuntimeHelpers).GetMethod("GetSource", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitRegExpGetFlags(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpGetFlags",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.Object]
        );
        runtime.RegExpGetFlags = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RegExpRuntimeHelpers).GetMethod("GetFlags", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitRegExpGetGlobal(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpGetGlobal",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.RegExpGetGlobal = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RegExpRuntimeHelpers).GetMethod("GetGlobal", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitRegExpGetIgnoreCase(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpGetIgnoreCase",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.RegExpGetIgnoreCase = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RegExpRuntimeHelpers).GetMethod("GetIgnoreCase", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitRegExpGetMultiline(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpGetMultiline",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            [_types.Object]
        );
        runtime.RegExpGetMultiline = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RegExpRuntimeHelpers).GetMethod("GetMultiline", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitRegExpGetLastIndex(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpGetLastIndex",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.Object]
        );
        runtime.RegExpGetLastIndex = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(RegExpRuntimeHelpers).GetMethod("GetLastIndex", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitRegExpSetLastIndex(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "RegExpSetLastIndex",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Double]
        );
        runtime.RegExpSetLastIndex = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RegExpRuntimeHelpers).GetMethod("SetLastIndex", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringMatchRegExp(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringMatchRegExp",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.String, _types.Object]
        );
        runtime.StringMatchRegExp = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RegExpRuntimeHelpers).GetMethod("StringMatch", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringReplaceRegExp(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringReplaceRegExp",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Object, _types.String]
        );
        runtime.StringReplaceRegExp = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(RegExpRuntimeHelpers).GetMethod("StringReplace", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringSearchRegExp(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringSearchRegExp",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Double,
            [_types.String, _types.Object]
        );
        runtime.StringSearchRegExp = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RegExpRuntimeHelpers).GetMethod("StringSearch", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitStringSplitRegExp(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "StringSplitRegExp",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.ListOfObject,
            [_types.String, _types.Object]
        );
        runtime.StringSplitRegExp = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(RegExpRuntimeHelpers).GetMethod("StringSplit", BindingFlags.Public | BindingFlags.Static)!);
        il.Emit(OpCodes.Ret);
    }
}

/// <summary>
/// Runtime helper methods for RegExp operations, called by emitted IL code.
/// </summary>
public static class RegExpRuntimeHelpers
{
    public static object Create(string pattern) => new SharpTSRegExp(pattern);

    public static object CreateWithFlags(string pattern, string flags) => new SharpTSRegExp(pattern, flags);

    public static bool Test(object? regex, string input) => (regex as SharpTSRegExp)?.Test(input) ?? false;

    public static object? Exec(object? regex, string input) => (regex as SharpTSRegExp)?.Exec(input);

    public static string ToString(object? regex) => (regex as SharpTSRegExp)?.ToString() ?? "/(?:)/";

    public static string GetSource(object? regex) => (regex as SharpTSRegExp)?.Source ?? "";

    public static string GetFlags(object? regex) => (regex as SharpTSRegExp)?.Flags ?? "";

    public static bool GetGlobal(object? regex) => (regex as SharpTSRegExp)?.Global ?? false;

    public static bool GetIgnoreCase(object? regex) => (regex as SharpTSRegExp)?.IgnoreCase ?? false;

    public static bool GetMultiline(object? regex) => (regex as SharpTSRegExp)?.Multiline ?? false;

    public static double GetLastIndex(object? regex) => (regex as SharpTSRegExp)?.LastIndex ?? 0;

    public static void SetLastIndex(object? regex, double value)
    {
        if (regex is SharpTSRegExp r) r.LastIndex = (int)value;
    }

    public static object? StringMatch(string str, object? pattern)
    {
        if (pattern is SharpTSRegExp regex)
        {
            if (regex.Global)
            {
                var matches = regex.MatchAll(str);
                if (matches.Count == 0) return null;
                return new SharpTSArray(matches.Select(m => (object?)m).ToList());
            }
            return regex.Exec(str);
        }
        var search = pattern?.ToString() ?? "";
        var idx = str.IndexOf(search);
        return idx >= 0 ? new SharpTSArray([(object?)search]) : null;
    }

    public static string StringReplace(string str, object? pattern, string replacement)
    {
        if (pattern is SharpTSRegExp regex)
        {
            return regex.Replace(str, replacement);
        }
        var search = pattern?.ToString() ?? "";
        var idx = str.IndexOf(search);
        return idx >= 0
            ? str.Substring(0, idx) + replacement + str.Substring(idx + search.Length)
            : str;
    }

    public static double StringSearch(string str, object? pattern)
    {
        if (pattern is SharpTSRegExp regex)
        {
            return regex.Search(str);
        }
        return str.IndexOf(pattern?.ToString() ?? "");
    }

    public static List<object> StringSplit(string str, object? separator)
    {
        if (separator is SharpTSRegExp regex)
        {
            return regex.Split(str).Select(s => (object)s).ToList();
        }
        var sep = separator?.ToString() ?? "";
        var parts = sep == ""
            ? str.Select(c => c.ToString()).ToArray()
            : str.Split(sep);
        return parts.Select(s => (object)s).ToList();
    }
}
