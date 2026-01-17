using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;

namespace SharpTS.Compilation;

/// <summary>
/// Emits the $RegExp class for standalone RegExp support.
/// NOTE: Must stay in sync with SharpTS.Runtime.Types.SharpTSRegExp
/// </summary>
public partial class RuntimeEmitter
{
    // $RegExp fields
    private FieldBuilder _tsRegExpRegexField = null!;
    private FieldBuilder _tsRegExpSourceField = null!;
    private FieldBuilder _tsRegExpFlagsField = null!;
    private FieldBuilder _tsRegExpGlobalField = null!;
    private FieldBuilder _tsRegExpIgnoreCaseField = null!;
    private FieldBuilder _tsRegExpMultilineField = null!;
    private FieldBuilder _tsRegExpLastIndexField = null!;

    // $RegExp internal methods
    private MethodBuilder _tsRegExpMatchAllMethod = null!;
    private MethodBuilder _tsRegExpReplaceMethod = null!;
    private MethodBuilder _tsRegExpSearchMethod = null!;
    private MethodBuilder _tsRegExpSplitMethod = null!;

    private void EmitTSRegExpClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $RegExp
        var typeBuilder = moduleBuilder.DefineType(
            "$RegExp",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.TSRegExpType = typeBuilder;

        // Fields
        _tsRegExpRegexField = typeBuilder.DefineField("_regex", typeof(Regex), FieldAttributes.Private);
        _tsRegExpSourceField = typeBuilder.DefineField("_source", _types.String, FieldAttributes.Private);
        _tsRegExpFlagsField = typeBuilder.DefineField("_flags", _types.String, FieldAttributes.Private);
        _tsRegExpGlobalField = typeBuilder.DefineField("_global", _types.Boolean, FieldAttributes.Private);
        _tsRegExpIgnoreCaseField = typeBuilder.DefineField("_ignoreCase", _types.Boolean, FieldAttributes.Private);
        _tsRegExpMultilineField = typeBuilder.DefineField("_multiline", _types.Boolean, FieldAttributes.Private);
        _tsRegExpLastIndexField = typeBuilder.DefineField("_lastIndex", _types.Int32, FieldAttributes.Public);

        // Helper method for normalizing flags
        EmitTSRegExpNormalizeFlags(typeBuilder, runtime);

        // Constructors (pattern+flags first because pattern-only calls it)
        EmitTSRegExpCtorPatternFlags(typeBuilder, runtime);
        EmitTSRegExpCtorPattern(typeBuilder, runtime);

        // Property getters
        EmitTSRegExpSourceGetter(typeBuilder, runtime);
        EmitTSRegExpFlagsGetter(typeBuilder, runtime);
        EmitTSRegExpGlobalGetter(typeBuilder, runtime);
        EmitTSRegExpIgnoreCaseGetter(typeBuilder, runtime);
        EmitTSRegExpMultilineGetter(typeBuilder, runtime);
        EmitTSRegExpLastIndexGetter(typeBuilder, runtime);
        EmitTSRegExpLastIndexSetter(typeBuilder, runtime);

        // Instance methods
        EmitTSRegExpTest(typeBuilder, runtime);
        EmitTSRegExpExec(typeBuilder, runtime);
        EmitTSRegExpToStringMethod(typeBuilder, runtime);

        // Internal methods for string operations
        EmitTSRegExpMatchAll(typeBuilder, runtime);
        EmitTSRegExpReplace(typeBuilder, runtime);
        EmitTSRegExpSearch(typeBuilder, runtime);
        EmitTSRegExpSplit(typeBuilder, runtime);

        typeBuilder.CreateType();
    }

    private void EmitTSRegExpNormalizeFlags(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // private static string NormalizeFlags(string flags)
        var method = typeBuilder.DefineMethod(
            "NormalizeFlags",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [_types.String]
        );
        runtime.TSRegExpNormalizeFlags = method;

        var il = method.GetILGenerator();
        var sbLocal = il.DeclareLocal(typeof(StringBuilder));

        // var sb = new StringBuilder()
        il.Emit(OpCodes.Newobj, typeof(StringBuilder).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, sbLocal);

        // if (flags.Contains('g')) sb.Append('g')
        EmitAppendFlagIfContains(il, sbLocal, 'g');
        EmitAppendFlagIfContains(il, sbLocal, 'i');
        EmitAppendFlagIfContains(il, sbLocal, 'm');

        // return sb.ToString()
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("ToString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitAppendFlagIfContains(ILGenerator il, LocalBuilder sbLocal, char flag)
    {
        var skipLabel = il.DefineLabel();

        // if (flags.Contains('x'))
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4, (int)flag);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Contains", [_types.Char])!);
        il.Emit(OpCodes.Brfalse, skipLabel);

        // sb.Append('x')
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldc_I4, (int)flag);
        il.Emit(OpCodes.Callvirt, typeof(StringBuilder).GetMethod("Append", [_types.Char])!);
        il.Emit(OpCodes.Pop);

        il.MarkLabel(skipLabel);
    }

    private void EmitTSRegExpCtorPattern(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public $RegExp(string pattern) : this(pattern, "") { }
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String]
        );
        runtime.TSRegExpCtorPattern = ctor;

        var il = ctor.GetILGenerator();

        // Call the two-arg constructor with empty string for flags
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);  // pattern
        il.Emit(OpCodes.Ldstr, "");  // flags = ""
        il.Emit(OpCodes.Call, runtime.TSRegExpCtorPatternFlags!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpCtorPatternFlags(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public $RegExp(string pattern, string flags)
        var ctor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.String, _types.String]
        );
        runtime.TSRegExpCtorPatternFlags = ctor;

        var il = ctor.GetILGenerator();
        var optionsLocal = il.DeclareLocal(typeof(RegexOptions));
        var tryEndLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // Call base constructor
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.Object.GetConstructor(Type.EmptyTypes)!);

        // _source = pattern
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsRegExpSourceField);

        // _flags = NormalizeFlags(flags)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.TSRegExpNormalizeFlags!);
        il.Emit(OpCodes.Stfld, _tsRegExpFlagsField);

        // _global = _flags.Contains('g')
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpFlagsField);
        il.Emit(OpCodes.Ldc_I4, (int)'g');
        il.Emit(OpCodes.Call, _types.String.GetMethod("Contains", [_types.Char])!);
        il.Emit(OpCodes.Stfld, _tsRegExpGlobalField);

        // _ignoreCase = _flags.Contains('i')
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpFlagsField);
        il.Emit(OpCodes.Ldc_I4, (int)'i');
        il.Emit(OpCodes.Call, _types.String.GetMethod("Contains", [_types.Char])!);
        il.Emit(OpCodes.Stfld, _tsRegExpIgnoreCaseField);

        // _multiline = _flags.Contains('m')
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpFlagsField);
        il.Emit(OpCodes.Ldc_I4, (int)'m');
        il.Emit(OpCodes.Call, _types.String.GetMethod("Contains", [_types.Char])!);
        il.Emit(OpCodes.Stfld, _tsRegExpMultilineField);

        // _lastIndex = 0
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsRegExpLastIndexField);

        // var options = RegexOptions.ECMAScript
        il.Emit(OpCodes.Ldc_I4, (int)RegexOptions.ECMAScript);
        il.Emit(OpCodes.Stloc, optionsLocal);

        // if (_ignoreCase) options |= RegexOptions.IgnoreCase
        var skipIgnoreCaseLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpIgnoreCaseField);
        il.Emit(OpCodes.Brfalse, skipIgnoreCaseLabel);
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Ldc_I4, (int)RegexOptions.IgnoreCase);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Stloc, optionsLocal);
        il.MarkLabel(skipIgnoreCaseLabel);

        // if (_multiline) options |= RegexOptions.Multiline
        var skipMultilineLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpMultilineField);
        il.Emit(OpCodes.Brfalse, skipMultilineLabel);
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Ldc_I4, (int)RegexOptions.Multiline);
        il.Emit(OpCodes.Or);
        il.Emit(OpCodes.Stloc, optionsLocal);
        il.MarkLabel(skipMultilineLabel);

        // try { _regex = new Regex(pattern, options); }
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);  // pattern
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Newobj, typeof(Regex).GetConstructor([_types.String, typeof(RegexOptions)])!);
        il.Emit(OpCodes.Stfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Leave, endLabel);

        // catch (ArgumentException ex) { throw new Exception("Invalid regular expression: " + ex.Message); }
        il.BeginCatchBlock(typeof(ArgumentException));
        var exLocal = il.DeclareLocal(typeof(ArgumentException));
        il.Emit(OpCodes.Stloc, exLocal);
        il.Emit(OpCodes.Ldstr, "Invalid regular expression: ");
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, typeof(Exception).GetProperty("Message")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String])!);
        il.Emit(OpCodes.Newobj, _types.Exception.GetConstructor([_types.String])!);
        il.Emit(OpCodes.Throw);

        il.EndExceptionBlock();

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpSourceGetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "get_Source",
            MethodAttributes.Public,
            _types.String,
            Type.EmptyTypes
        );
        runtime.TSRegExpSourceGetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpSourceField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpFlagsGetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "get_Flags",
            MethodAttributes.Public,
            _types.String,
            Type.EmptyTypes
        );
        runtime.TSRegExpFlagsGetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpFlagsField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpGlobalGetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "get_Global",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        runtime.TSRegExpGlobalGetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpGlobalField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpIgnoreCaseGetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "get_IgnoreCase",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        runtime.TSRegExpIgnoreCaseGetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpIgnoreCaseField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpMultilineGetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "get_Multiline",
            MethodAttributes.Public,
            _types.Boolean,
            Type.EmptyTypes
        );
        runtime.TSRegExpMultilineGetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpMultilineField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpLastIndexGetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "get_LastIndex",
            MethodAttributes.Public,
            _types.Int32,
            Type.EmptyTypes
        );
        runtime.TSRegExpLastIndexGetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpLastIndexField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpLastIndexSetter(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "set_LastIndex",
            MethodAttributes.Public,
            _types.Void,
            [_types.Int32]
        );
        runtime.TSRegExpLastIndexSetter = method;

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stfld, _tsRegExpLastIndexField);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpTest(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public bool Test(string input)
        var method = typeBuilder.DefineMethod(
            "Test",
            MethodAttributes.Public,
            _types.Boolean,
            [_types.String]
        );
        runtime.TSRegExpTestMethod = method;

        var il = method.GetILGenerator();
        var globalLabel = il.DefineLabel();
        var matchFoundLabel = il.DefineLabel();
        var notGlobalLabel = il.DefineLabel();
        var returnFalseLabel = il.DefineLabel();
        var matchLocal = il.DeclareLocal(typeof(Match));
        var startIndexLocal = il.DeclareLocal(_types.Int32);

        // if (_global) goto globalLabel
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpGlobalField);
        il.Emit(OpCodes.Brtrue, globalLabel);

        // Non-global: return _regex.IsMatch(input)
        il.MarkLabel(notGlobalLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(Regex).GetMethod("IsMatch", [_types.String])!);
        il.Emit(OpCodes.Ret);

        // Global path
        il.MarkLabel(globalLabel);

        // if (LastIndex > input.Length) { LastIndex = 0; return false; }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpLastIndexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ble, returnFalseLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsRegExpLastIndexField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnFalseLabel);

        // var startIndex = Math.Min(LastIndex, input.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpLastIndexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [_types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Stloc, startIndexLocal);

        // var match = _regex.Match(input, startIndex)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, startIndexLocal);
        il.Emit(OpCodes.Callvirt, typeof(Regex).GetMethod("Match", [_types.String, _types.Int32])!);
        il.Emit(OpCodes.Stloc, matchLocal);

        // if (match.Success)
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Match).GetProperty("Success")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, matchFoundLabel);

        // No match: LastIndex = 0; return false
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsRegExpLastIndexField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        // Match found: LastIndex = match.Index + match.Length; return true
        il.MarkLabel(matchFoundLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Index")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stfld, _tsRegExpLastIndexField);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpExec(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public object? Exec(string input) - returns $Array or null
        var method = typeBuilder.DefineMethod(
            "Exec",
            MethodAttributes.Public,
            _types.Object,
            [_types.String]
        );
        runtime.TSRegExpExecMethod = method;

        var il = method.GetILGenerator();
        var globalMatchLabel = il.DefineLabel();
        var nonGlobalMatchLabel = il.DefineLabel();
        var checkSuccessLabel = il.DefineLabel();
        var matchSuccessLabel = il.DefineLabel();
        var noMatchLabel = il.DefineLabel();
        var buildResultLabel = il.DefineLabel();
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();

        var matchLocal = il.DeclareLocal(typeof(Match));
        var startIndexLocal = il.DeclareLocal(_types.Int32);
        var elementsLocal = il.DeclareLocal(_types.ListOfObject);
        var iLocal = il.DeclareLocal(_types.Int32);
        var groupLocal = il.DeclareLocal(typeof(Group));

        // if (_global)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpGlobalField);
        il.Emit(OpCodes.Brtrue, globalMatchLabel);

        // Non-global: match = _regex.Match(input)
        il.MarkLabel(nonGlobalMatchLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(Regex).GetMethod("Match", [_types.String])!);
        il.Emit(OpCodes.Stloc, matchLocal);
        il.Emit(OpCodes.Br, checkSuccessLabel);

        // Global path
        il.MarkLabel(globalMatchLabel);

        // if (LastIndex > input.Length) { LastIndex = 0; return null; }
        var continueGlobalLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpLastIndexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Ble, continueGlobalLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsRegExpLastIndexField);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(continueGlobalLabel);

        // startIndex = Math.Min(LastIndex, input.Length)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpLastIndexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.String.GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Min", [_types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Stloc, startIndexLocal);

        // match = _regex.Match(input, startIndex)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, startIndexLocal);
        il.Emit(OpCodes.Callvirt, typeof(Regex).GetMethod("Match", [_types.String, _types.Int32])!);
        il.Emit(OpCodes.Stloc, matchLocal);

        // Check if match was successful
        il.MarkLabel(checkSuccessLabel);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Match).GetProperty("Success")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, matchSuccessLabel);

        // No match
        il.MarkLabel(noMatchLabel);

        // if (_global) LastIndex = 0
        var skipResetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpGlobalField);
        il.Emit(OpCodes.Brfalse, skipResetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, _tsRegExpLastIndexField);
        il.MarkLabel(skipResetLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // Match success
        il.MarkLabel(matchSuccessLabel);

        // if (_global) LastIndex = match.Index + match.Length
        var skipUpdateLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpGlobalField);
        il.Emit(OpCodes.Brfalse, skipUpdateLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Index")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Length")!.GetGetMethod()!);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stfld, _tsRegExpLastIndexField);
        il.MarkLabel(skipUpdateLabel);

        // Build result array
        // var elements = new List<object?>()
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, elementsLocal);

        // elements.Add(match.Value)
        il.Emit(OpCodes.Ldloc, elementsLocal);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add", [_types.Object])!);

        // Add capture groups (skip group 0 which is the full match)
        // for (int i = 1; i < match.Groups.Count; i++)
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(loopStartLabel);
        // i < match.Groups.Count
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Match).GetProperty("Groups")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(GroupCollection).GetProperty("Count")!.GetGetMethod()!);
        il.Emit(OpCodes.Bge, loopEndLabel);

        // var group = match.Groups[i]
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Match).GetProperty("Groups")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, typeof(GroupCollection).GetMethod("get_Item", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, groupLocal);

        // elements.Add(group.Success ? group.Value : null)
        var groupSuccessLabel = il.DefineLabel();
        var addGroupEndLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, elementsLocal);
        il.Emit(OpCodes.Ldloc, groupLocal);
        il.Emit(OpCodes.Callvirt, typeof(Group).GetProperty("Success")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, groupSuccessLabel);

        // Not success: add null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Br, addGroupEndLabel);

        // Success: add value
        il.MarkLabel(groupSuccessLabel);
        il.Emit(OpCodes.Ldloc, groupLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Value")!.GetGetMethod()!);

        il.MarkLabel(addGroupEndLabel);
        il.Emit(OpCodes.Callvirt, _types.ListOfObject.GetMethod("Add", [_types.Object])!);

        // i++
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);

        // return new $Array(elements)
        il.Emit(OpCodes.Ldloc, elementsLocal);
        il.Emit(OpCodes.Newobj, runtime.TSArrayCtor!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpToStringMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // public override string ToString() => $"/{_source}/{_flags}"
        var method = typeBuilder.DefineMethod(
            "ToString",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            _types.String,
            Type.EmptyTypes
        );
        runtime.TSRegExpToStringMethod = method;

        var il = method.GetILGenerator();

        // "/" + _source + "/" + _flags
        il.Emit(OpCodes.Ldstr, "/");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpSourceField);
        il.Emit(OpCodes.Ldstr, "/");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpFlagsField);
        il.Emit(OpCodes.Call, _types.String.GetMethod("Concat", [_types.String, _types.String, _types.String, _types.String])!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpMatchAll(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // internal List<string> MatchAll(string input)
        var method = typeBuilder.DefineMethod(
            "MatchAll",
            MethodAttributes.Assembly,
            typeof(List<string>),
            [_types.String]
        );
        _tsRegExpMatchAllMethod = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(typeof(List<string>));
        var matchesLocal = il.DeclareLocal(typeof(MatchCollection));
        var enumeratorLocal = il.DeclareLocal(typeof(System.Collections.IEnumerator));
        var loopStartLabel = il.DefineLabel();
        var loopEndLabel = il.DefineLabel();
        var finallyLabel = il.DefineLabel();

        // var result = new List<string>()
        il.Emit(OpCodes.Newobj, typeof(List<string>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, resultLocal);

        // var matches = _regex.Matches(input)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(Regex).GetMethod("Matches", [_types.String])!);
        il.Emit(OpCodes.Stloc, matchesLocal);

        // foreach (Match m in matches) { result.Add(m.Value); }
        il.Emit(OpCodes.Ldloc, matchesLocal);
        il.Emit(OpCodes.Callvirt, typeof(MatchCollection).GetMethod("GetEnumerator")!);
        il.Emit(OpCodes.Stloc, enumeratorLocal);

        il.BeginExceptionBlock();

        il.MarkLabel(loopStartLabel);
        il.Emit(OpCodes.Ldloc, enumeratorLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.IEnumerator).GetMethod("MoveNext")!);
        il.Emit(OpCodes.Brfalse, loopEndLabel);

        // result.Add(((Match)enumerator.Current).Value)
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ldloc, enumeratorLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Collections.IEnumerator).GetProperty("Current")!.GetGetMethod()!);
        il.Emit(OpCodes.Castclass, typeof(Match));
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Value")!.GetGetMethod()!);
        il.Emit(OpCodes.Callvirt, typeof(List<string>).GetMethod("Add", [_types.String])!);
        il.Emit(OpCodes.Br, loopStartLabel);

        il.MarkLabel(loopEndLabel);
        il.Emit(OpCodes.Leave, finallyLabel);

        // Finally block to dispose if IDisposable
        il.BeginFinallyBlock();
        var disposeEndLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, enumeratorLocal);
        il.Emit(OpCodes.Isinst, typeof(IDisposable));
        il.Emit(OpCodes.Brfalse, disposeEndLabel);
        il.Emit(OpCodes.Ldloc, enumeratorLocal);
        il.Emit(OpCodes.Castclass, typeof(IDisposable));
        il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose")!);
        il.MarkLabel(disposeEndLabel);
        il.EndExceptionBlock();

        il.MarkLabel(finallyLabel);

        // return result
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpReplace(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // internal string Replace(string input, string replacement)
        var method = typeBuilder.DefineMethod(
            "Replace",
            MethodAttributes.Assembly,
            _types.String,
            [_types.String, _types.String]
        );
        _tsRegExpReplaceMethod = method;

        var il = method.GetILGenerator();
        var globalLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        // if (_global)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpGlobalField);
        il.Emit(OpCodes.Brtrue, globalLabel);

        // Non-global: return _regex.Replace(input, replacement, 1)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, typeof(Regex).GetMethod("Replace", [_types.String, _types.String, _types.Int32])!);
        il.Emit(OpCodes.Ret);

        // Global: return _regex.Replace(input, replacement)
        il.MarkLabel(globalLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, typeof(Regex).GetMethod("Replace", [_types.String, _types.String])!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpSearch(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // internal int Search(string input)
        var method = typeBuilder.DefineMethod(
            "Search",
            MethodAttributes.Assembly,
            _types.Int32,
            [_types.String]
        );
        _tsRegExpSearchMethod = method;

        var il = method.GetILGenerator();
        var matchLocal = il.DeclareLocal(typeof(Match));
        var successLabel = il.DefineLabel();

        // var match = _regex.Match(input)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(Regex).GetMethod("Match", [_types.String])!);
        il.Emit(OpCodes.Stloc, matchLocal);

        // return match.Success ? match.Index : -1
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Match).GetProperty("Success")!.GetGetMethod()!);
        il.Emit(OpCodes.Brtrue, successLabel);

        il.Emit(OpCodes.Ldc_I4_M1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(successLabel);
        il.Emit(OpCodes.Ldloc, matchLocal);
        il.Emit(OpCodes.Callvirt, typeof(Capture).GetProperty("Index")!.GetGetMethod()!);
        il.Emit(OpCodes.Ret);
    }

    private void EmitTSRegExpSplit(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // internal string[] Split(string input)
        var method = typeBuilder.DefineMethod(
            "Split",
            MethodAttributes.Assembly,
            typeof(string[]),
            [_types.String]
        );
        _tsRegExpSplitMethod = method;

        var il = method.GetILGenerator();

        // return _regex.Split(input)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, _tsRegExpRegexField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, typeof(Regex).GetMethod("Split", [_types.String])!);
        il.Emit(OpCodes.Ret);
    }
}
