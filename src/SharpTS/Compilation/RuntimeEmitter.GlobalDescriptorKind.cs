using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitGlobalThisIntrinsicDescriptorKind(EmittedGlobalObjectRuntime globalObject)
    {
        var il = globalObject.IntrinsicDescriptorKind.GetILGenerator();
        var writable = il.DefineLabel();
        var constant = il.DefineLabel();
        // This is the existing JavaScript descriptor-name table. It carries
        // property attributes only, with no CLR type or callable admission.
        foreach (var name in new[] { "parseInt", "parseFloat", "isNaN", "isFinite", "eval",
            "globalThis", "Array", "ArrayBuffer", "Date", "RegExp", "Map", "Set", "WeakMap", "WeakSet",
            "Promise", "Function", "Object", "Number", "String", "Boolean", "Symbol", "Error", "TypeError",
            "RangeError", "ReferenceError", "SyntaxError", "URIError", "EvalError", "AggregateError", "Math",
            "JSON", "AbortController" })
            EmitName(name, writable);
        foreach (var name in new[] { "NaN", "Infinity", "undefined" }) EmitName(name, constant);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(writable);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(constant);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        void EmitName(string name, Label matched)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brtrue, matched);
        }
    }
}