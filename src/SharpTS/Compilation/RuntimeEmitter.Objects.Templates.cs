using System;
using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitConcatTemplate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "ConcatTemplate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.ObjectArray]
        );
        runtime.ConcatTemplate = method;

        var il = method.GetILGenerator();

        // Use StringBuilder to concatenate stringified parts
        var sbLocal = il.DeclareLocal(_types.StringBuilder);
        var indexLocal = il.DeclareLocal(_types.Int32);
        var lengthLocal = il.DeclareLocal(_types.Int32);

        // sb = new StringBuilder()
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.StringBuilder));
        il.Emit(OpCodes.Stloc, sbLocal);

        // length = parts.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Stloc, lengthLocal);

        // index = 0
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();

        il.MarkLabel(loopStart);
        // if (index >= length) goto end
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldloc, lengthLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // sb.Append(StringifyCoerce(parts[index])) — template-literal
        // interpolation is an implicit ToString coercion, so Symbol parts
        // throw TypeError (§7.1.17).
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, runtime.StringifyCoerce);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.StringBuilder, "Append", _types.String));
        il.Emit(OpCodes.Pop); // discard StringBuilder return value

        // index++
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(loopEnd);
        // return sb.ToString()
        il.Emit(OpCodes.Ldloc, sbLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.StringBuilder, "ToString"));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits the $TemplateStringsList class for tagged template literals.
    /// This is a List&lt;object&gt; subclass with a "raw" property for accessing raw strings.
    /// </summary>
    internal void EmitTemplateStringsListClass(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        // Define class: public sealed class $TemplateStringsList : List<object>
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$TemplateStringsList",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.ListOfObject
        );

        // Field: private readonly List<object> _rawStrings
        var rawStringsField = typeBuilder.DefineField(
            "_rawStrings",
            _types.ListOfObject,
            FieldAttributes.Private | FieldAttributes.InitOnly
        );

        // Constructor: public $TemplateStringsList(object[] cookedStrings, string[] rawStrings)
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.ObjectArray, _types.StringArray]
        );

        var ctorIL = ctorBuilder.GetILGenerator();

        // Call base constructor: List<object>()
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.ListOfObject));

        // Add cooked strings to the list
        // foreach (var s in cookedStrings) Add(s ?? "undefined")
        var iLocal = ctorIL.DeclareLocal(_types.Int32);
        var cookedLoopStart = ctorIL.DefineLabel();
        var cookedLoopEnd = ctorIL.DefineLabel();

        ctorIL.Emit(OpCodes.Ldc_I4_0);
        ctorIL.Emit(OpCodes.Stloc, iLocal);

        ctorIL.MarkLabel(cookedLoopStart);
        ctorIL.Emit(OpCodes.Ldloc, iLocal);
        ctorIL.Emit(OpCodes.Ldarg_1); // cookedStrings
        ctorIL.Emit(OpCodes.Ldlen);
        ctorIL.Emit(OpCodes.Conv_I4);
        ctorIL.Emit(OpCodes.Bge, cookedLoopEnd);

        // this.Add(cookedStrings[i] ?? "undefined")
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Ldloc, iLocal);
        ctorIL.Emit(OpCodes.Ldelem_Ref);

        // If null, replace with "undefined"
        var notNullLabel = ctorIL.DefineLabel();
        var afterNullCheck = ctorIL.DefineLabel();
        ctorIL.Emit(OpCodes.Dup);
        ctorIL.Emit(OpCodes.Brtrue, notNullLabel);
        ctorIL.Emit(OpCodes.Pop);
        ctorIL.Emit(OpCodes.Ldstr, "undefined");
        ctorIL.Emit(OpCodes.Br, afterNullCheck);
        ctorIL.MarkLabel(notNullLabel);
        ctorIL.MarkLabel(afterNullCheck);

        ctorIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        ctorIL.Emit(OpCodes.Ldloc, iLocal);
        ctorIL.Emit(OpCodes.Ldc_I4_1);
        ctorIL.Emit(OpCodes.Add);
        ctorIL.Emit(OpCodes.Stloc, iLocal);
        ctorIL.Emit(OpCodes.Br, cookedLoopStart);

        ctorIL.MarkLabel(cookedLoopEnd);

        // Create _rawStrings list from rawStrings array
        // _rawStrings = new List<object>()
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.EmptyTypes));
        ctorIL.Emit(OpCodes.Stfld, rawStringsField);

        // foreach (var s in rawStrings) _rawStrings.Add(s)
        var rawLoopStart = ctorIL.DefineLabel();
        var rawLoopEnd = ctorIL.DefineLabel();

        ctorIL.Emit(OpCodes.Ldc_I4_0);
        ctorIL.Emit(OpCodes.Stloc, iLocal);

        ctorIL.MarkLabel(rawLoopStart);
        ctorIL.Emit(OpCodes.Ldloc, iLocal);
        ctorIL.Emit(OpCodes.Ldarg_2); // rawStrings
        ctorIL.Emit(OpCodes.Ldlen);
        ctorIL.Emit(OpCodes.Conv_I4);
        ctorIL.Emit(OpCodes.Bge, rawLoopEnd);

        // _rawStrings.Add(rawStrings[i])
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldfld, rawStringsField);
        ctorIL.Emit(OpCodes.Ldarg_2);
        ctorIL.Emit(OpCodes.Ldloc, iLocal);
        ctorIL.Emit(OpCodes.Ldelem_Ref);
        ctorIL.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", _types.Object));

        ctorIL.Emit(OpCodes.Ldloc, iLocal);
        ctorIL.Emit(OpCodes.Ldc_I4_1);
        ctorIL.Emit(OpCodes.Add);
        ctorIL.Emit(OpCodes.Stloc, iLocal);
        ctorIL.Emit(OpCodes.Br, rawLoopStart);

        ctorIL.MarkLabel(rawLoopEnd);
        ctorIL.Emit(OpCodes.Ret);

        // Property: public List<object> raw => _rawStrings
        var rawGetter = typeBuilder.DefineMethod(
            "get_raw",
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
            _types.ListOfObject,
            Type.EmptyTypes
        );
        var rawGetterIL = rawGetter.GetILGenerator();
        rawGetterIL.Emit(OpCodes.Ldarg_0);
        rawGetterIL.Emit(OpCodes.Ldfld, rawStringsField);
        rawGetterIL.Emit(OpCodes.Ret);

        var rawProp = typeBuilder.DefineProperty(
            "raw",
            PropertyAttributes.None,
            _types.ListOfObject,
            null
        );
        rawProp.SetGetMethod(rawGetter);

        // Create the type
        var createdType = typeBuilder.CreateType()!;
        runtime.TemplateStringsListType = createdType;
        runtime.TemplateStringsListCtor = createdType.GetConstructor([_types.ObjectArray, _types.StringArray])!;
        runtime.TemplateStringsListRawGetter = createdType.GetProperty("raw")!.GetGetMethod()!;
    }

    private void EmitInvokeTaggedTemplate(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // InvokeTaggedTemplate(tag: object, cooked: object[], raw: string[], expressions: object[]) -> object?
        var method = typeBuilder.DefineMethod(
            "InvokeTaggedTemplate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.ObjectArray, _types.StringArray, _types.ObjectArray]
        );
        runtime.InvokeTaggedTemplate = method;

        var il = method.GetILGenerator();

        // Create strings array: new $TemplateStringsList(cooked, raw)
        var stringsLocal = il.DeclareLocal(runtime.TemplateStringsListType);
        il.Emit(OpCodes.Ldarg_1); // cooked
        il.Emit(OpCodes.Ldarg_2); // raw
        il.Emit(OpCodes.Newobj, runtime.TemplateStringsListCtor);
        il.Emit(OpCodes.Stloc, stringsLocal);

        // Freeze the template strings array and its raw property (JS spec: frozen/immutable)
        il.Emit(OpCodes.Ldloc, stringsLocal);
        il.Emit(OpCodes.Call, runtime.ObjectFreeze);
        il.Emit(OpCodes.Pop); // ObjectFreeze returns the object, discard
        il.Emit(OpCodes.Ldloc, stringsLocal);
        il.Emit(OpCodes.Callvirt, runtime.TemplateStringsListRawGetter);
        il.Emit(OpCodes.Call, runtime.ObjectFreeze);
        il.Emit(OpCodes.Pop); // discard

        // Build args array: new object[1 + expressions.Length]
        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_3); // expressions
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);

        // args[0] = stringsArray
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, stringsLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // Copy expressions to args[1..]
        // for (int i = 0; i < expressions.Length; i++) args[i + 1] = expressions[i]
        var iLocal = il.DeclareLocal(_types.Int32);
        var copyLoopStart = il.DefineLabel();
        var copyLoopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(copyLoopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg_3); // expressions
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, copyLoopEnd);

        // args[i + 1] = expressions[i]
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, copyLoopStart);

        il.MarkLabel(copyLoopEnd);

        // tag must be non-null and callable
        var errorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, errorLabel);

        // return InvokeValue(tag, args)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeValue);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(errorLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Tagged template tag must be a function.");
    }

    /// <summary>
    /// Emits InvokeTaggedTemplateWithThis - like InvokeTaggedTemplate but passes a this binding.
    /// Signature: object? InvokeTaggedTemplateWithThis(tag, thisArg, cooked, raw, expressions)
    /// </summary>
    private void EmitInvokeTaggedTemplateWithThis(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "InvokeTaggedTemplateWithThis",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object, _types.ObjectArray, _types.StringArray, _types.ObjectArray]
        );
        runtime.InvokeTaggedTemplateWithThis = method;

        var il = method.GetILGenerator();

        // Create strings array: new $TemplateStringsList(cooked, raw)
        var stringsLocal = il.DeclareLocal(runtime.TemplateStringsListType);
        il.Emit(OpCodes.Ldarg_2); // cooked
        il.Emit(OpCodes.Ldarg_3); // raw
        il.Emit(OpCodes.Newobj, runtime.TemplateStringsListCtor);
        il.Emit(OpCodes.Stloc, stringsLocal);

        // Freeze the template strings array and its raw property (JS spec)
        il.Emit(OpCodes.Ldloc, stringsLocal);
        il.Emit(OpCodes.Call, runtime.ObjectFreeze);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldloc, stringsLocal);
        il.Emit(OpCodes.Callvirt, runtime.TemplateStringsListRawGetter);
        il.Emit(OpCodes.Call, runtime.ObjectFreeze);
        il.Emit(OpCodes.Pop);

        // Build args array: new object[1 + expressions.Length]
        var argsLocal = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg, 4); // expressions (arg index 4)
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, argsLocal);

        // args[0] = stringsArray
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, stringsLocal);
        il.Emit(OpCodes.Stelem_Ref);

        // Copy expressions to args[1..]
        var iLocal = il.DeclareLocal(_types.Int32);
        var copyLoopStart = il.DefineLabel();
        var copyLoopEnd = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);

        il.MarkLabel(copyLoopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldarg, 4); // expressions
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, copyLoopEnd);

        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldarg, 4);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stelem_Ref);

        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, copyLoopStart);

        il.MarkLabel(copyLoopEnd);

        // tag must be non-null and callable
        var errorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, errorLabel);

        // return InvokeMethodValue(thisArg, tag, args)
        il.Emit(OpCodes.Ldarg_1); // thisArg (receiver)
        il.Emit(OpCodes.Ldarg_0); // tag (function)
        il.Emit(OpCodes.Ldloc, argsLocal);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(errorLabel);
        GuestErrorEmitter.ThrowTypeError(il, runtime, "Tagged template tag must be a function.");
    }
}
