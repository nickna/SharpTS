using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    internal void EmitToPascalCase(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // ToPascalCase(string name) -> string
        // Converts "camelCase" to "PascalCase" by upper-casing first character
        var method = typeBuilder.DefineMethod(
            "ToPascalCase",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.String,
            [_types.String]
        );
        runtime.ToPascalCase = method;

        var il = method.GetILGenerator();
        var returnOriginalLabel = il.DefineLabel();

        // if (string.IsNullOrEmpty(name)) return name;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "IsNullOrEmpty"));
        il.Emit(OpCodes.Brtrue, returnOriginalLabel);

        // if (char.IsUpper(name[0])) return name;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Call, typeof(char).GetMethod("IsUpper", [typeof(char)])!);
        il.Emit(OpCodes.Brtrue, returnOriginalLabel);

        // return char.ToString(char.ToUpperInvariant(name[0])) + name.Substring(1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        il.Emit(OpCodes.Call, typeof(char).GetMethod("ToUpperInvariant", [typeof(char)])!);
        il.Emit(OpCodes.Call, typeof(char).GetMethod("ToString", [typeof(char)])!);  // static char.ToString(char)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Substring", _types.Int32));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Concat", _types.String, _types.String));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnOriginalLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits a reflection helper <c>SafeGetMethod(Type, string, BindingFlags) -> MethodInfo</c>
    /// that wraps <see cref="Type.GetMethod(string, BindingFlags)"/> and degrades gracefully
    /// when the lookup would otherwise throw <see cref="System.Reflection.AmbiguousMatchException"/>
    /// because multiple overloads share the name. On ambiguity: prefer a zero-argument
    /// overload (matches the "read property, invoke with no args" pattern used by
    /// <c>GetFieldsProperty</c>'s callable wrapping); otherwise return the first
    /// name-matching overload. Returns null when no method matches the name.
    /// </summary>
    internal void EmitSafeGetMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SafeGetMethod",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.MethodInfo,
            [_types.Type, _types.String, typeof(BindingFlags)]
        );
        runtime.SafeGetMethod = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.MethodInfo);
        var methodsArrayType = _types.MakeArrayType(_types.MethodInfo);
        var methodsLocal = il.DeclareLocal(methodsArrayType);
        var iLocal = il.DeclareLocal(_types.Int32);
        var mLocal = il.DeclareLocal(_types.MethodInfo);

        var retLabel = il.DefineLabel();

        // Happy path: return t.GetMethod(name, flags) when unambiguous.
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String, typeof(BindingFlags)));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, retLabel);

        // Ambiguous — fall back to a deterministic pick.
        il.BeginCatchBlock(typeof(System.Reflection.AmbiguousMatchException));
        il.Emit(OpCodes.Pop); // discard exception
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stloc, resultLocal);

        // methods = t.GetMethods(flags)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethods", typeof(BindingFlags)));
        il.Emit(OpCodes.Stloc, methodsLocal);

        // Pass 1: prefer a zero-arg overload.
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var pass1Start = il.DefineLabel();
        var pass1End = il.DefineLabel();
        var pass1Continue = il.DefineLabel();
        il.MarkLabel(pass1Start);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, methodsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, pass1End);

        il.Emit(OpCodes.Ldloc, methodsLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, mLocal);

        // if (!m.Name.Equals(name, OrdinalIgnoreCase)) continue
        il.Emit(OpCodes.Ldloc, mLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.MethodBase, "Name"));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, (int)StringComparison.OrdinalIgnoreCase);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", _types.String, _types.String, _types.StringComparison));
        il.Emit(OpCodes.Brfalse, pass1Continue);

        // if (m.GetParameters().Length != 0) continue
        il.Emit(OpCodes.Ldloc, mLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodBase, "GetParameters"));
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Brtrue, pass1Continue);

        // Zero-arg match — store and break out of pass 1.
        il.Emit(OpCodes.Ldloc, mLocal);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, pass1End);

        il.MarkLabel(pass1Continue);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, pass1Start);
        il.MarkLabel(pass1End);

        // If pass 1 found something, we're done.
        var catchEnd = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Brtrue, catchEnd);

        // Pass 2: first name match (arbitrary but deterministic).
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        var pass2Start = il.DefineLabel();
        var pass2End = il.DefineLabel();
        var pass2Continue = il.DefineLabel();
        il.MarkLabel(pass2Start);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, methodsLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Bge, pass2End);

        il.Emit(OpCodes.Ldloc, methodsLocal);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, mLocal);

        il.Emit(OpCodes.Ldloc, mLocal);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.MethodBase, "Name"));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4, (int)StringComparison.OrdinalIgnoreCase);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "Equals", _types.String, _types.String, _types.StringComparison));
        il.Emit(OpCodes.Brfalse, pass2Continue);

        il.Emit(OpCodes.Ldloc, mLocal);
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Br, pass2End);

        il.MarkLabel(pass2Continue);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, pass2Start);
        il.MarkLabel(pass2End);

        il.MarkLabel(catchEnd);
        il.Emit(OpCodes.Leave, retLabel);
        il.EndExceptionBlock();

        il.MarkLabel(retLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetFieldsProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // GetFieldsProperty(object obj, string name) -> object
        // Resolves class-instance properties through emitted runtime state only:
        // descriptor store accessors, emitted $Object fields, and known method wrappers.
        var method = typeBuilder.DefineMethod(
            "GetFieldsProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.String]
        );
        runtime.GetFieldsProperty = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var tryMethodLabel = il.DefineLabel();

        // Declare locals upfront
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var valueLocal = il.DeclareLocal(_types.Object);
        var objectFieldsLocal = il.DeclareLocal(_types.DictionaryStringObject);

        // if (obj == null) return null;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // ECMA-262: `error.constructor` should return the constructor that built it.
        // For $Error subclasses (and $Error itself), return the instance's runtime
        // type as a System.Type. Compiled-mode `TypeError` resolves to the same
        // System.Type, so `caught.constructor === TypeError` strict-equality works.
        // Without this, test262's `assert.throws(TypeError, fn)` fails because
        // `thrown.constructor === expectedErrorConstructor` reads `undefined`.
        var afterErrorCtorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSErrorType);
        il.Emit(OpCodes.Brfalse, afterErrorCtorLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, afterErrorCtorLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(afterErrorCtorLabel);

        // ECMA-262: `(42).constructor === Number`, `true.constructor === Boolean`.
        // Compiled `Number` resolves to typeof(double); `Boolean` to typeof(bool).
        // (String is handled by EmitGetProperty's EmitStringGetBranch arm.)
        var afterPrimCtorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, afterPrimCtorLabel);
        // double?
        var notDoubleCtorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notDoubleCtorLabel);
        il.Emit(OpCodes.Ldtoken, _types.Double);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notDoubleCtorLabel);
        // bool?
        var notBoolCtorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, notBoolCtorLabel);
        il.Emit(OpCodes.Ldtoken, _types.Boolean);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBoolCtorLabel);
        il.MarkLabel(afterPrimCtorLabel);

        // Special case: HashSet<object?>.size for compiled Sets
        // Compiled code uses HashSet<object?> for Sets, and structuredClone returns the same type.
        // When accessing .size, we need to return HashSet.Count.
        var hashSetSizeLabel = il.DefineLabel();
        var afterHashSetSizeLabel = il.DefineLabel();

        // Check if obj is HashSet<object?> and name == "size"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.HashSetOfObject);
        il.Emit(OpCodes.Brfalse, afterHashSetSizeLabel);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "size");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, afterHashSetSizeLabel);

        // It's a HashSet and property is "size" - return Count as double
        il.MarkLabel(hashSetSizeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.HashSetOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.HashSetOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(afterHashSetSizeLabel);

        // Check $PropertyDescriptorStore for dynamically defined properties (via Object.defineProperty)
        // This allows defineProperty to work on class instances
        var afterPDSCheckLabel = il.DefineLabel();
        var pdsGetterLocal = il.DeclareLocal(_types.Object);
        var pdsDescriptorLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);

        // Try to get getter: PDSTryGetGetter(obj, name, out getter)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, pdsGetterLocal);
        il.Emit(OpCodes.Call, runtime.PDSTryGetGetter);
        var noGetterInPDSLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, noGetterInPDSLabel);

        // Getter was found - invoke it: InvokeMethodValue(obj, getter, emptyArgs)
        il.Emit(OpCodes.Ldarg_0);  // receiver (obj)
        il.Emit(OpCodes.Ldloc, pdsGetterLocal);  // function (getter)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);  // empty args array
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noGetterInPDSLabel);

        // Try to get descriptor: PDSGetPropertyDescriptor(obj, name)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, pdsDescriptorLocal);

        // If descriptor is null, continue to next checks
        il.Emit(OpCodes.Ldloc, pdsDescriptorLocal);
        il.Emit(OpCodes.Brfalse, afterPDSCheckLabel);

        // Descriptor found - return descriptor.Value
        il.Emit(OpCodes.Ldloc, pdsDescriptorLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(afterPDSCheckLabel);

        // If obj is emitted $Object, query its _fields dictionary directly.
        // If property not found in _fields, fall through to $IHasFields check
        // (since $Object subclasses may have typed properties with backing fields)
        var notTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObjectLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectFieldsGetter);
        il.Emit(OpCodes.Stloc, objectFieldsLocal);

        il.Emit(OpCodes.Ldloc, objectFieldsLocal);
        il.Emit(OpCodes.Brfalse, notTSObjectLabel);  // Changed: check $IHasFields if _fields is null

        il.Emit(OpCodes.Ldloc, objectFieldsLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        il.Emit(OpCodes.Brfalse, notTSObjectLabel);  // Changed: check $IHasFields if not in _fields
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notTSObjectLabel);

        // Check plain Dictionary<string, object?> (vm.Script objects, CreateObject results, etc.)
        var notDictLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brfalse, notDictLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        il.Emit(OpCodes.Brfalse, notDictLabel);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notDictLabel);

        // Check $IHasFields interface (covers user-defined classes and $Object subclasses with typed properties)
        var notHasFieldsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, notHasFieldsLabel);

        // Call interface method: ((IHasFields)obj).GetProperty(name)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsGetProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notHasFieldsLabel);

        // Check $Error - handle native own properties.  Any remaining name
        // must continue directly on the JavaScript prototype chain: exposing
        // CLR methods here (notably $Error.ToString) would shadow mutable
        // Error.prototype properties.
        var errorPrototypeLookupLabel = il.DefineLabel();
        var notErrorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSErrorType);
        il.Emit(OpCodes.Brfalse, notErrorLabel);

        // Check "name"
        var notErrorNameLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notErrorNameLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorNameGetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notErrorNameLabel);

        // Check "message"
        var notErrorMessageLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notErrorMessageLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorMessageGetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notErrorMessageLabel);

        // Check "stack"
        var notErrorStackLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "stack");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notErrorStackLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorStackGetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notErrorStackLabel);

        // Check "code" — only return if non-null (absent on plain Error objects)
        var notErrorCodeNameLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "code");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notErrorCodeNameLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorCodeGetter);
        var codeNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, codeNullLabel);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(codeNullLabel);
        il.Emit(OpCodes.Pop); // discard null from Dup
        il.MarkLabel(notErrorCodeNameLabel);

        // Check "syscall" — only return if non-null
        var notErrorSyscallNameLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "syscall");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notErrorSyscallNameLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorSyscallGetter);
        var syscallNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, syscallNullLabel);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(syscallNullLabel);
        il.Emit(OpCodes.Pop); // discard null from Dup
        il.MarkLabel(notErrorSyscallNameLabel);

        // AggregateError has one additional native own property.  Keep it in
        // the explicit Error dispatch so bypassing CLR reflection does not hide
        // the rejection list from dynamically-typed/caught values.
        var notAggregateErrorsNameLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "errors");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notAggregateErrorsNameLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSAggregateErrorType);
        il.Emit(OpCodes.Brfalse, notAggregateErrorsNameLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSAggregateErrorType);
        il.Emit(OpCodes.Callvirt, runtime.TSAggregateErrorErrorsGetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notAggregateErrorsNameLabel);

        il.Emit(OpCodes.Br, errorPrototypeLookupLabel);

        il.MarkLabel(notErrorLabel);

        // $StringDecoder dispatch removed — StringDecoder migrated to
        // stdlib/node/string_decoder.ts. Its instances now go through the
        // standard user-class property dispatch path like any other TS class.

        // Try to find a method with this name and wrap as TSFunction
        il.MarkLabel(tryMethodLabel);

        // First try array methods if it's an array
        var noArrayMethodLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetArrayMethod);
        var arrayMethodLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, arrayMethodLocal);
        il.Emit(OpCodes.Ldloc, arrayMethodLocal);
        il.Emit(OpCodes.Brfalse, noArrayMethodLabel);
        il.Emit(OpCodes.Ldloc, arrayMethodLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noArrayMethodLabel);

        // Skip all .NET reflection fallbacks for System.Type instances. An
        // emitted `Array` / user-class token bound to a local becomes a
        // System.Type at runtime; `P_0.GetType()` returns RuntimeType, whose
        // .NET properties (IsArray, IsClass, Name, FullName, …) would
        // otherwise bleed through as JS property values — notably making
        // `var f = Array.isArray` resolve to the boolean `false` because
        // IgnoreCase matches System.Type.IsArray and returns its value
        // for typeof(IList<object>). The legitimate static-member lookups
        // for Type live in EmitGetProperty's Type branch (static method
        // → $TSFunction, static field → value, "name" → type.Name) and
        // already ran before falling through here; no further reflection
        // is correct for a Type. Returning $Undefined.Instance matches
        // ECMAScript §7.3.2 Get for absent properties.
        var skipTypeReflectionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brtrue, skipTypeReflectionLabel);

        // Fallback: Try reflection-based property access for runtime-emitted types
        // This handles types like $Readable, $Writable, $Duplex that don't implement $IHasFields

        // Convert camelCase name to PascalCase for .NET property lookup
        // e.g., "readable" -> "Readable", "readableEnded" -> "ReadableEnded"
        var pascalNameLocal = il.DeclareLocal(_types.String);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.ToPascalCase);
        il.Emit(OpCodes.Stloc, pascalNameLocal);

        // Try to get property: obj.GetType().GetProperty(pascalName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)
        var propertyInfoLocal = il.DeclareLocal(_types.PropertyInfo);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldloc, pascalNameLocal);
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase));
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetProperty", _types.String, typeof(System.Reflection.BindingFlags)));
        il.Emit(OpCodes.Stloc, propertyInfoLocal);

        // If property found, call getter
        var noPropertyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, propertyInfoLocal);
        il.Emit(OpCodes.Brfalse, noPropertyLabel);

        // return propertyInfo.GetValue(obj)
        il.Emit(OpCodes.Ldloc, propertyInfoLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.PropertyInfo, "GetValue", _types.Object));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noPropertyLabel);

        // Fallback: Try reflection-based method lookup for runtime-emitted types
        // This handles methods like Push, Pipe, etc. on $Readable, $Writable, $Dir, etc.
        // Uses SafeGetMethod so overloaded methods (e.g. Guid.ToString, StringBuilder.Append)
        // don't crash with AmbiguousMatchException.
        var methodInfoLocal = il.DeclareLocal(_types.MethodInfo);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldloc, pascalNameLocal);
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase));
        il.Emit(OpCodes.Call, runtime.SafeGetMethod);
        il.Emit(OpCodes.Stloc, methodInfoLocal);

        // If method found, wrap in $TSFunction and return
        var noMethodLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, methodInfoLocal);
        il.Emit(OpCodes.Brfalse, noMethodLabel);

        // Wrap in $TSFunction: new $TSFunction(target, methodInfo)
        il.Emit(OpCodes.Ldarg_0);  // target object
        il.Emit(OpCodes.Ldloc, methodInfoLocal);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noMethodLabel);

        // Fallback: Try GetMember(string) method for types like $DiffieHellman, $ECDH
        // that expose properties only through their GetMember dispatch method.
        var getMemberLocal = il.DeclareLocal(_types.MethodInfo);
        var noGetMemberLabel = il.DefineLabel();

        // Guard: if obj is a System.Type (e.g. a compiled class reference used as
        // a dynamic target), its inherited GetMember overloads cause
        // AmbiguousMatchException from GetMethod(name, flags). Skip the fallback
        // for Type instances — their PropertyDescriptorStore entries (if any)
        // were already consulted above.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brtrue, noGetMemberLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "GetMember");
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public));
        il.Emit(OpCodes.Call, runtime.SafeGetMethod);
        il.Emit(OpCodes.Stloc, getMemberLocal);

        il.Emit(OpCodes.Ldloc, getMemberLocal);
        il.Emit(OpCodes.Brfalse, noGetMemberLabel);

        // Call GetMember(name): methodInfo.Invoke(obj, new object[] { name })
        var getMemberResultLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, getMemberLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1); // name
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Stloc, getMemberResultLocal);

        // If result is null, fall through to undefined
        var getMemberNullLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, getMemberResultLocal);
        il.Emit(OpCodes.Brfalse, getMemberNullLabel);

        // If result is $TSFunction or $BoundTSFunction, return as-is (fast path)
        var returnCallableAsIsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, getMemberResultLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue_S, returnCallableAsIsLabel);
        il.Emit(OpCodes.Ldloc, getMemberResultLocal);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue_S, returnCallableAsIsLabel);

        // Check if result has a "Call" method — if so it's a callable (BuiltInMethod etc.)
        // and should be wrapped in $MethodCallable for dispatch through InvokeMethodValue.
        // Objects without "Call" (property values like SearchParams) are returned as-is.
        var returnAsIsLabel = il.DefineLabel();
        var callMethodLocal = il.DeclareLocal(_types.MethodInfo);
        il.Emit(OpCodes.Ldloc, getMemberResultLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "Call");
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public));
        il.Emit(OpCodes.Call, runtime.SafeGetMethod);
        il.Emit(OpCodes.Stloc, callMethodLocal);
        il.Emit(OpCodes.Ldloc, callMethodLocal);
        il.Emit(OpCodes.Brfalse, returnAsIsLabel);

        // Has "Call" method → wrap in $MethodCallable
        il.Emit(OpCodes.Ldloc, getMemberResultLocal);
        il.Emit(OpCodes.Newobj, runtime.MethodCallableCtor);
        il.Emit(OpCodes.Ret);

        // Return $TSFunction/$BoundTSFunction as-is
        il.MarkLabel(returnCallableAsIsLabel);
        il.Emit(OpCodes.Ldloc, getMemberResultLocal);
        il.Emit(OpCodes.Ret);

        // Return non-callable values as-is
        il.MarkLabel(returnAsIsLabel);
        il.Emit(OpCodes.Ldloc, getMemberResultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(getMemberNullLabel);

        il.MarkLabel(skipTypeReflectionLabel);
        il.MarkLabel(noGetMemberLabel);

        // Ordinary [[Get]] continues on the receiver's [[Prototype]] after
        // all own-property mechanisms miss. Specialized GetProperty arms for
        // intrinsic CLR-backed objects delegate here, but this helper used to
        // return undefined immediately; arbitrary inherited properties on
        // RegExp.prototype, Error.prototype, Promise.prototype, and explicit
        // Object.setPrototypeOf targets were consequently invisible.
        il.MarkLabel(errorPrototypeLookupLabel);
        var noPrototypeLabel = il.DefineLabel();
        var prototypeLocal = il.DeclareLocal(_types.Object);
        // Internal runtime probes historically use GetProperty(undefined, k)
        // as a non-throwing "not present" check (notably awaitable coercion).
        // Preserve that contract rather than passing the sentinel to the
        // public Object.getPrototypeOf semantics, which correctly throw.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, noPrototypeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ObjectGetPrototypeOf);
        il.Emit(OpCodes.Stloc, prototypeLocal);
        il.Emit(OpCodes.Ldloc, prototypeLocal);
        il.Emit(OpCodes.Brfalse, noPrototypeLabel);
        // Guard malformed host/custom prototype cycles from recursing forever.
        il.Emit(OpCodes.Ldloc, prototypeLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Beq, noPrototypeLabel);
        il.Emit(OpCodes.Ldloc, prototypeLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noPrototypeLabel);
        il.MarkLabel(nullLabel);
        // Return $Undefined.Instance for non-existent properties (JavaScript semantics)
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetListProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // GetListProperty(list: List<object>, name: string) -> object?
        // Returns length as double, or a $BoundArrayMethod for array methods
        var method = typeBuilder.DefineMethod(
            "GetListProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.ListOfObject, _types.String]
        );
        runtime.GetListProperty = method;

        var il = method.GetILGenerator();

        var lengthLabel = il.DefineLabel();
        var returnNullLabel = il.DefineLabel();
        var createBoundMethodLabel = il.DefineLabel();

        // if (name == "length") goto lengthLabel
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, lengthLabel);

        // if (name == "raw") - check for TemplateStringsList.raw property
        var rawLabel = il.DefineLabel();
        var skipRawLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "raw");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, rawLabel);
        il.Emit(OpCodes.Br, skipRawLabel);

        il.MarkLabel(rawLabel);
        // For tagged template arrays, call emitted TemplateStringsList.raw getter.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TemplateStringsListType);
        il.Emit(OpCodes.Brfalse, returnNullLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TemplateStringsListType);
        il.Emit(OpCodes.Callvirt, runtime.TemplateStringsListRawGetter);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(skipRawLabel);

        // Check for known array method names
        // For each method name, if match, create $BoundArrayMethod
        // Must stay in sync with EmitBoundArrayMethodFinalize's dispatch switch
        // (RuntimeEmitter.Arrays.cs) and ArrayEmitter.cs static dispatch.
        string[] methodNames = [
            "join", "push", "pop", "shift", "unshift", "slice", "splice",
            "indexOf", "lastIndexOf", "includes", "concat", "reverse", "sort", "map", "filter", "forEach",
            "find", "findIndex", "findLast", "findLastIndex", "some", "every",
            "reduce", "reduceRight",
            "flat", "flatMap", "at",
            "toSorted", "toSpliced", "toReversed", "with",
            "fill", "copyWithin",
            "entries", "keys", "values",
            "toString", "toLocaleString"
        ];

        foreach (var methodName in methodNames)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, methodName);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brfalse, skipLabel);

            // Create $BoundArrayMethod(list, name) and return
            il.Emit(OpCodes.Ldarg_0); // list
            il.Emit(OpCodes.Ldarg_1); // name
            il.Emit(OpCodes.Newobj, runtime.BoundArrayMethodCtor);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(skipLabel);
        }

        // No known array method match — check PropertyDescriptorStore for custom defined properties
        il.MarkLabel(returnNullLabel);
        var reallyNullLabel = il.DefineLabel();
        var pdsDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_0); // list (as object)
        il.Emit(OpCodes.Ldarg_1); // name
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, pdsDescLocal);
        il.Emit(OpCodes.Ldloc, pdsDescLocal);
        il.Emit(OpCodes.Brfalse, reallyNullLabel);
        // Return descriptor.Value
        il.Emit(OpCodes.Ldloc, pdsDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
        il.Emit(OpCodes.Ret);

        // Final fallback: walk Array.prototype, then Object.prototype singleton
        // dicts. ECMA-262 says a List receiver inherits from %Array.prototype%
        // which inherits from %Object.prototype%, so user-added entries
        // (`Array.prototype.foo = 1`) reach indexed-access reads as
        // `arr.foo === 1`, and Object.prototype methods like hasOwnProperty,
        // toString, valueOf flow through too. Populate both prototype dicts
        // if not yet populated.
        il.MarkLabel(reallyNullLabel);
        var arrayProtoFallbackLabel = il.DefineLabel();
        var objectProtoFallbackLabel = il.DefineLabel();
        il.Emit(OpCodes.Call, runtime.ArrayPrototypePopulateMethod);
        var arrayProtoValLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.ArrayPrototypeField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, arrayProtoValLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, arrayProtoFallbackLabel);
        il.Emit(OpCodes.Ldloc, arrayProtoValLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(arrayProtoFallbackLabel);
        // Walk to Object.prototype for shared methods like hasOwnProperty.
        il.Emit(OpCodes.Call, runtime.ObjectPrototypePopulateMethod);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, arrayProtoValLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, objectProtoFallbackLabel);
        il.Emit(OpCodes.Ldloc, arrayProtoValLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(objectProtoFallbackLabel);
        // Missing-property reads should return JS undefined, not C# null —
        // tests assert `arr.foo === undefined` not `arr.foo === null`.
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        // length case: return (double)list.Count — except for $Arguments,
        // which exposes its own _length field (sloppy arguments objects
        // don't auto-update length on out-of-range indexed writes per
        // ECMA-262 10.4.4).
        il.MarkLabel(lengthLabel);
        var notArgumentsLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.ArgumentsType);
        il.Emit(OpCodes.Brfalse, notArgumentsLengthLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.ArgumentsType);
        il.Emit(OpCodes.Ldfld, runtime.ArgumentsLengthField);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notArgumentsLengthLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.ListOfObject, "Count"));
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetMapProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // GetMapProperty(map: Dictionary<object,object>, name: string) -> object?
        // Returns size as double, or a $BoundMapMethod wrapper for known Map methods.
        // Mirrors GetListProperty — ensures duck typing works across module boundaries:
        // typeof map.get === 'function' and map.get.call(map, k) both work on a Map
        // received from another module.
        var method = typeBuilder.DefineMethod(
            "GetMapProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.DictionaryObjectObject, _types.String]
        );
        runtime.GetMapProperty = method;

        var il = method.GetILGenerator();

        var sizeLabel = il.DefineLabel();

        // if (name == "size") goto sizeLabel
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "size");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, sizeLabel);

        // For each known Map method name, return a $BoundMapMethod wrapper.
        string[] methodNames = ["get", "set", "has", "delete", "clear",
            "keys", "values", "entries", "forEach"];

        foreach (var methodName in methodNames)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, methodName);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brfalse, skipLabel);

            il.Emit(OpCodes.Ldarg_0); // map
            il.Emit(OpCodes.Ldarg_1); // name
            il.Emit(OpCodes.Newobj, runtime.BoundMapMethodCtor);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(skipLabel);
        }

        // Unknown property: return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // size case: return (double)map.Count
        il.MarkLabel(sizeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryObjectObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetSetProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // GetSetProperty(set: HashSet<object>, name: string) -> object?
        // Returns size as double, or a $BoundSetMethod wrapper for known Set methods
        // (including ES2025 set operations). Mirrors GetListProperty / GetMapProperty.
        var method = typeBuilder.DefineMethod(
            "GetSetProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.HashSetOfObject, _types.String]
        );
        runtime.GetSetProperty = method;

        var il = method.GetILGenerator();

        var sizeLabel = il.DefineLabel();

        // if (name == "size") goto sizeLabel
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "size");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brtrue, sizeLabel);

        // For each known Set method name, return a $BoundSetMethod wrapper.
        string[] methodNames = ["add", "has", "delete", "clear",
            "keys", "values", "entries", "forEach",
            "union", "intersection", "difference", "symmetricDifference",
            "isSubsetOf", "isSupersetOf", "isDisjointFrom"];

        foreach (var methodName in methodNames)
        {
            var skipLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, methodName);
            il.Emit(OpCodes.Call, _types.StringOpEquality);
            il.Emit(OpCodes.Brfalse, skipLabel);

            il.Emit(OpCodes.Ldarg_0); // set
            il.Emit(OpCodes.Ldarg_1); // name
            il.Emit(OpCodes.Newobj, runtime.BoundSetMethodCtor);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(skipLabel);
        }

        // Unknown property: return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // size case: return (double)set.Count
        il.MarkLabel(sizeLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.HashSetOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
    }

    private void EmitGetProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Signature forward-declared by DefineRuntimeClassPhase1.
        var method = (MethodBuilder)runtime.GetProperty;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();

        // null check
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // globalThis/global sentinel (#271): a value-position globalThis reads
        // properties through GlobalThisGetProperty (user props → built-in
        // constructors/singletons), so `root.Object`/`root.Math` resolve to real
        // values. Checked first so the bare-object sentinel never falls through to
        // the class-instance handler (which would report every member undefined).
        var notGlobalThisLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldsfld, runtime.GlobalThisSingletonField);
        il.Emit(OpCodes.Bne_Un, notGlobalThisLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GlobalThisGetProperty);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notGlobalThisLabel);

        // __proto__ accessor (ECMA-262 Annex B.2.2.1): obj.__proto__ delegates
        // to Object.getPrototypeOf(obj). All object types support this — the
        // accessor lives on Object.prototype, but intercepting here avoids
        // replicating the dispatch in every object-specific branch. Without
        // this, `{}.__proto__` returns undefined and breaks spec idioms.
        //
        // CAVEAT: ECMA-262 also allows defining an own "__proto__" data
        // property that shadows the inherited accessor. JSON.parse creates
        // such own data properties (CreateDataProperty). For Dict receivers,
        // check ContainsKey first and fall through to the regular dict path
        // when the key is present — preserves JSON.parse semantics for the
        // `{"__proto__":...}` corner.
        var notProtoNameTopLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "__proto__");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notProtoNameTopLabel);
        // Dict + ContainsKey("__proto__") → skip intercept.
        var protoDictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, protoDictLocal);
        var noOwnProtoLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, protoDictLocal);
        il.Emit(OpCodes.Brfalse, noOwnProtoLabel);
        il.Emit(OpCodes.Ldloc, protoDictLocal);
        il.Emit(OpCodes.Ldstr, "__proto__");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Brtrue, notProtoNameTopLabel);
        il.MarkLabel(noOwnProtoLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.ObjectGetPrototypeOf);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notProtoNameTopLabel);

        // Proxy check: uses obj.GetType().FullName comparison (no SharpTS.dll dependency)
        var notProxyLabel = il.DefineLabel();
        EmitProxyGetPropertyCheck(il, runtime, () => il.Emit(OpCodes.Ldarg_0), () => il.Emit(OpCodes.Ldarg_1), notProxyLabel);

        il.MarkLabel(notProxyLabel);

        // $TSNamespace - call ns.Get(name)
        var notNamespaceLabel = il.DefineLabel();
        EmitNamespaceGetBranch(il, runtime, notNamespaceLabel);
        il.MarkLabel(notNamespaceLabel);

        // $Object (with getter/setter support) - obj.GetProperty(name) + PDS + proto walk.
        var notTSObjectLabel = il.DefineLabel();
        EmitTSObjectGetBranch(il, runtime, method, notTSObjectLabel);
        il.MarkLabel(notTSObjectLabel);

        // Map (Dictionary<object, object>) - "size" + $BoundMapMethod wrappers.
        if (_features.UsesMap)
        {
            var notMapLabel = il.DefineLabel();
            EmitMapGetBranch(il, runtime, notMapLabel);
            il.MarkLabel(notMapLabel);
        }

        // Set (HashSet<object>) - duck-typed access via GetSetProperty.
        if (_features.UsesSet)
        {
            var notSetLabel = il.DefineLabel();
            EmitSetGetBranch(il, runtime, notSetLabel);
            il.MarkLabel(notSetLabel);
        }

        // Dictionary (regular object) - own entries, PDS, prototype chain, Object.prototype.
        var notDictLabel = il.DefineLabel();
        EmitDictGetBranch(il, runtime, method, notDictLabel);
        il.MarkLabel(notDictLabel);

        // User Array subclass (#233): a guest class extending Array derives
        // from $Array AND implements $IHasFields. Its class members (declared
        // fields, getters, methods) take precedence over the built-in array
        // surface; the per-class GetProperty returns $Undefined on miss, in
        // which case we fall through to the ordinary $Array dispatch below.
        var notArraySubclassLabel = il.DefineLabel();
        var arraySubclassMissLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brfalse, notArraySubclassLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, notArraySubclassLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsGetProperty);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Beq, arraySubclassMissLabel);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(arraySubclassMissLabel);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(notArraySubclassLabel);

        // $Array - "length"/"constructor"/index/methods. MUST come BEFORE the plain
        // List arm so sparse-aware length is used ($Array inherits List<object?>);
        // otherwise `new Array(10_000_000).length` returns 0.
        var notSharpTSArrayLabel = il.DefineLabel();
        EmitSharpTSArrayGetBranch(il, runtime, notSharpTSArrayLabel);
        il.MarkLabel(notSharpTSArrayLabel);

        // List - "length"/"constructor"/index/methods.
        var notListLabel = il.DefineLabel();
        EmitListGetBranch(il, runtime, notListLabel);
        il.MarkLabel(notListLabel);

        // object[] (compiled `arguments` representation) - "length"/index.
        var notObjectArrayLabel = il.DefineLabel();
        EmitObjectArrayGetBranch(il, runtime, notObjectArrayLabel);
        il.MarkLabel(notObjectArrayLabel);

        // String - "length"/"constructor"/index/String.prototype methods.
        var notStringArmLabel = il.DefineLabel();
        EmitStringGetBranch(il, runtime, method, notStringArmLabel);
        il.MarkLabel(notStringArmLabel);

        // $Buffer - "length" and "toString". Only meaningful when some feature
        // emitted $Buffer (crypto/fs/zlib/http/fetch/dgram/net).
        if (_features.UsesBuffer)
        {
            var notBufferLabel = il.DefineLabel();
            EmitBufferGetBranch(il, runtime, notBufferLabel);
            il.MarkLabel(notBufferLabel);
        }

        // $Stats - isFile, isDirectory, size, etc. Only when fs is on.
        if (_features.UsesFs)
        {
            var notStatsLabel = il.DefineLabel();
            EmitStatsGetBranch(il, runtime, notStatsLabel);
            il.MarkLabel(notStatsLabel);
        }

        // $TSFunction - check for bind/call/apply
        var notTSFunctionLabel = il.DefineLabel();
        EmitFunctionGetBranch(il, runtime, runtime.TSFunctionType, notTSFunctionLabel);
        il.MarkLabel(notTSFunctionLabel);

        // $BoundTSFunction - also check for bind/call/apply
        var notBoundFunctionLabel = il.DefineLabel();
        EmitFunctionGetBranch(il, runtime, runtime.BoundTSFunctionType, notBoundFunctionLabel);
        il.MarkLabel(notBoundFunctionLabel);

        // $CJSModule - route to the module's GetMember(name) for exports/id/filename/etc.
        // Only emitted when the program uses CommonJS (require/module/exports).
        if (_features.UsesCjsRequire)
        {
            var notCjsModuleLabel = il.DefineLabel();
            EmitCjsModuleGetBranch(il, runtime, notCjsModuleLabel);
            il.MarkLabel(notCjsModuleLabel);
        }

        // $RegExp — surface the built-in slots (`lastIndex`, `source`, `flags`,
        // `global`, `ignoreCase`, `multiline`, `sticky`, `unicode`, `hasIndices`,
        // `dotAll`, `unicodeSets`) via the typed getters / parsed-from-flags
        // expressions. Without this branch the read falls through to
        // GetFieldsProperty whose reflection lookup is case-sensitive ("lastIndex"
        // vs "LastIndex") and silently returns undefined. Test262's
        // builtin-coerce-lastindex.js + many coerce/builtin-* tests require the
        // internal slot value to round-trip through `r.lastIndex` reads/writes.
        if (_features.UsesRegExp)
        {
            var notRegExpLabel = il.DefineLabel();
            EmitRegExpGetBranch(il, runtime, method, notRegExpLabel);
            il.MarkLabel(notRegExpLabel);
        }

        // Task<object?> (Promise) - check for then/catch/finally
        var promiseLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.TaskOfObject);
        il.Emit(OpCodes.Brtrue, promiseLabel);

        // User Promise subclass (#242): a guest class extending Promise
        // derives from $Promise AND implements $IHasFields. Its class members
        // (declared fields, getters, methods) take precedence over the
        // built-in promise surface; the per-class GetProperty returns
        // $Undefined on miss, in which case we fall through to the ordinary
        // $Promise dispatch below. Mirrors the $Array subclass arm above.
        var notPromiseSubclassLabel = il.DefineLabel();
        var promiseSubclassMissLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brfalse, notPromiseSubclassLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, notPromiseSubclassLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsGetProperty);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Beq, promiseSubclassMissLabel);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(promiseSubclassMissLabel);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(notPromiseSubclassLabel);

        // $Promise type (used by fetch, etc.) - check for then/catch/finally
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brtrue, promiseLabel);

        // ArrayBuffer / SharedArrayBuffer / DataView / TypedArray dispatch arms —
        // skipped when no typed-array kind is referenced. The handler bodies
        // (MarkLabel'd at lines ~1834+) are gated on the same flag.
        var arrayBufferLabel = il.DefineLabel();
        var sharedArrayBufferLabel = il.DefineLabel();
        var dataViewLabel = il.DefineLabel();
        var typedArrayLabel = il.DefineLabel();
        if (_features.HasAnyTypedArray)
        {
            // $ArrayBuffer - check for "byteLength"
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.ArrayBufferType);
            il.Emit(OpCodes.Brtrue, arrayBufferLabel);

            // $SharedArrayBuffer - check for "byteLength"
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.SharedArrayBufferType);
            il.Emit(OpCodes.Brtrue, sharedArrayBufferLabel);

            // $DataView - check for "byteLength", "byteOffset", "buffer"
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.DataViewType);
            il.Emit(OpCodes.Brtrue, dataViewLabel);

            // TypedArray - use emitted helper dispatch for standalone behavior
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.IsTypedArrayMethod);
            il.Emit(OpCodes.Brtrue, typedArrayLabel);
        }

        // Primitive bool/double receivers — look up the named property in the
        // matching prototype singleton (Boolean.prototype / Number.prototype).
        // ECMA-262 7.3.2 OrdinaryGetPrototypeOf treats every primitive as if
        // wrapped via ToObject, so `(true).valueOf` walks Boolean.prototype.
        // Without this branch, `b.valueOf` returned undefined for any-typed
        // bools because the routing fell through to classInstanceLabel which
        // can't resolve methods on a CLR `bool` value-type box.
        var notBoolPrimLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brfalse, notBoolPrimLabel);
        il.Emit(OpCodes.Call, runtime.BooleanPrototypePopulateMethod);
        // Preserve the original primitive as the receiver of an accessor
        // installed on Boolean.prototype. Recursing through GetProperty on the
        // prototype dictionary would invoke the getter with that dictionary as
        // `this`, making strict getters observe typeof this === "object".
        var boolProtoDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        var boolProtoGetterLocal = il.DeclareLocal(_types.Object);
        var boolProtoOrdinaryLookupLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, runtime.BooleanPrototypeField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, boolProtoDescLocal);
        il.Emit(OpCodes.Ldloc, boolProtoDescLocal);
        il.Emit(OpCodes.Brfalse, boolProtoOrdinaryLookupLabel);
        il.Emit(OpCodes.Ldloc, boolProtoDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, boolProtoGetterLocal);
        il.Emit(OpCodes.Ldloc, boolProtoGetterLocal);
        il.Emit(OpCodes.Brfalse, boolProtoOrdinaryLookupLabel);
        il.Emit(OpCodes.Ldloc, boolProtoGetterLocal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, boolProtoOrdinaryLookupLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, boolProtoGetterLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(boolProtoOrdinaryLookupLabel);
        il.Emit(OpCodes.Ldsfld, runtime.BooleanPrototypeField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, method);  // recursive GetProperty lookup on the dict
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBoolPrimLabel);

        var notDoublePrimLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brfalse, notDoublePrimLabel);
        il.Emit(OpCodes.Call, runtime.NumberPrototypePopulateMethod);
        il.Emit(OpCodes.Ldsfld, runtime.NumberPrototypeField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notDoublePrimLabel);

        // $Bound*Method and $BoundAnyFunction - callable wrappers that need .call/.apply/.bind
        // support. Route through GetFunctionMethod which handles bind/call/apply/length/name.
        // Bound methods already capture their receiver, so thisArg passed to .call/.apply is
        // ignored per JS bound-callable semantics — the CallWrapper/ApplyWrapper Invoke bodies
        // implement that via EmitDispatchToTarget.
        var callableWrapperLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.BoundArrayMethodType);
        il.Emit(OpCodes.Brtrue, callableWrapperLabel);
        if (_features.UsesMap)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.BoundMapMethodType);
            il.Emit(OpCodes.Brtrue, callableWrapperLabel);
        }
        if (_features.UsesSet)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.BoundSetMethodType);
            il.Emit(OpCodes.Brtrue, callableWrapperLabel);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.BoundAnyFunctionType);
        il.Emit(OpCodes.Brtrue, callableWrapperLabel);

        // System.Type (a class reference used as a value, e.g. `Scalar.PLAIN = 'x'` then
        // reading `Scalar.PLAIN`). JS allows arbitrary static property assignment on classes;
        // we store them in $PropertyDescriptorStore. Check PDS first; if no descriptor, fall
        // through to class-instance resolver (which on a Type will read its .NET members).
        var typeGetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brtrue, typeGetLabel);

        // Default - try class-instance fields/property resolution helper
        var classInstanceLabel = il.DefineLabel();
        il.Emit(OpCodes.Br, classInstanceLabel);

        // Class instance handler
        il.MarkLabel(classInstanceLabel);
        // Call GetFieldsProperty(obj, name) helper
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetFieldsProperty);
        il.Emit(OpCodes.Ret);

        // System.Type handler: check PropertyDescriptorStore first for user-added static
        // properties (`ClassName.foo = 'bar'`). Then look up declared static methods,
        // fields, and accessors on the .NET Type via reflection. Only falls through to
        // the class-instance handler (which returns Undefined for Type) if none match.
        //
        // Without the reflection step, `const Alias = Foo; Alias.bar()` and
        // `require('./mod').Cls.staticMethod()` silently bind to undefined — compiled
        // classes are emitted as System.Type tokens, so static access must walk the Type.
        il.MarkLabel(typeGetLabel);
        {
            // Boolean/Number/String.prototype — return the per-type singleton
            // Dictionary so writes/reads round-trip. Stage 4w: required for
            // test262 patterns like `Boolean.prototype[0] = true; Boolean.prototype.length = 1;
            // Array.prototype.every.call(false, cb)` to surface the customization
            // when the materializer falls back to the prototype for primitive
            // receivers. Check first, before PDS lookup, so the singleton wins
            // over any user-stored "prototype" descriptor.
            var notProtoNameLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "prototype");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, notProtoNameLabel);
            // typeof(object) is the value-form representation of the Object
            // constructor.  Its prototype is the real Object.prototype
            // singleton, not a reflected CLR member.
            var notObjectLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, _types.Object);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Bne_Un, notObjectLabel);
            il.Emit(OpCodes.Call, runtime.ObjectPrototypePopulateMethod);
            il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notObjectLabel);
            // typeof Boolean
            var notBoolLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, _types.Boolean);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Bne_Un, notBoolLabel);
            // Lazy-populate Boolean.prototype with $TSFunction wrappers on first read.
            il.Emit(OpCodes.Call, runtime.BooleanPrototypePopulateMethod);
            il.Emit(OpCodes.Ldsfld, runtime.BooleanPrototypeField);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notBoolLabel);
            var notDoubleLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, _types.Double);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Bne_Un, notDoubleLabel);
            // Lazy-populate Number.prototype with $TSFunction wrappers on first read.
            il.Emit(OpCodes.Call, runtime.NumberPrototypePopulateMethod);
            il.Emit(OpCodes.Ldsfld, runtime.NumberPrototypeField);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notDoubleLabel);
            var notBigIntLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, _types.BigInteger);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Bne_Un, notBigIntLabel);
            il.Emit(OpCodes.Ldsfld, runtime.BigIntPrototypeField);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notBigIntLabel);
            var notStringLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, _types.String);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Bne_Un, notStringLabel);
            // Lazy-populate String.prototype with $TSFunction wrappers on first read.
            il.Emit(OpCodes.Call, runtime.StringPrototypePopulateMethod);
            il.Emit(OpCodes.Ldsfld, runtime.StringPrototypeField);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notStringLabel);
            // typeof($Error) and its native-error subclasses → return the
            // matching prototype singleton. Each subclass has a *distinct*
            // prototype object per ECMA-262 §20.5.6.4 (TypeError.prototype !==
            // Error.prototype, etc.). The shell helpers populate constructor/
            // name/message lazily and wire [[Prototype]] to Error.prototype.
            void EmitErrorTypeBranch(Type ctorType, MethodBuilder populate, FieldBuilder protoField)
            {
                var notMatch = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldtoken, ctorType);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
                il.Emit(OpCodes.Bne_Un, notMatch);
                il.Emit(OpCodes.Call, populate);
                il.Emit(OpCodes.Ldsfld, protoField);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(notMatch);
            }
            EmitErrorTypeBranch(runtime.TSTypeErrorType,      runtime.TypeErrorPrototypePopulateMethod,      runtime.TypeErrorPrototypeField);
            EmitErrorTypeBranch(runtime.TSRangeErrorType,     runtime.RangeErrorPrototypePopulateMethod,     runtime.RangeErrorPrototypeField);
            EmitErrorTypeBranch(runtime.TSReferenceErrorType, runtime.ReferenceErrorPrototypePopulateMethod, runtime.ReferenceErrorPrototypeField);
            EmitErrorTypeBranch(runtime.TSSyntaxErrorType,    runtime.SyntaxErrorPrototypePopulateMethod,    runtime.SyntaxErrorPrototypeField);
            EmitErrorTypeBranch(runtime.TSURIErrorType,       runtime.URIErrorPrototypePopulateMethod,       runtime.URIErrorPrototypeField);
            EmitErrorTypeBranch(runtime.TSEvalErrorType,      runtime.EvalErrorPrototypePopulateMethod,      runtime.EvalErrorPrototypeField);
            EmitErrorTypeBranch(runtime.TSAggregateErrorType, runtime.AggregateErrorPrototypePopulateMethod, runtime.AggregateErrorPrototypeField);
            // Base Error last (its Type token is distinct from the subclass tokens).
            EmitErrorTypeBranch(runtime.TSErrorType, runtime.ErrorPrototypePopulateMethod, runtime.ErrorPrototypeField);

            // typeof($TSFunction) → return Function.prototype singleton.
            // Required so `Function.prototype.call.bind(...)` (test262
            // propertyHelper.js's first line) resolves; without this, the
            // harness errors at load and ~1200 tests show as RuntimeError.
            var notFunctionLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, runtime.TSFunctionType);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Bne_Un, notFunctionLabel);
            il.Emit(OpCodes.Call, runtime.FunctionPrototypePopulateMethod);
            il.Emit(OpCodes.Ldsfld, runtime.FunctionPrototypeField);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notFunctionLabel);

            // typeof(Task<object>) → return Promise.prototype singleton.
            // Hosts then/catch/finally + constructor pointer. Required for
            // Test262 patterns like `Promise.prototype.then instanceof Function`
            // and `typeof Promise.prototype.finally === "function"`.
            var notPromiseProtoLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, _types.TaskOfObject);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Bne_Un, notPromiseProtoLabel);
            il.Emit(OpCodes.Call, runtime.PromisePrototypePopulateMethod);
            il.Emit(OpCodes.Ldsfld, runtime.PromisePrototypeField);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notPromiseProtoLabel);

            // typeof($RegExp) → return RegExp.prototype singleton. Hosts the
            // five well-known-symbol-keyed methods (@@match, etc.) used by
            // ECMA-262 §22.2.5 protocol tests. Gated on UsesRegExp because
            // $RegExp itself is gated and the populate's referenced helpers
            // (TSRegExpSym*Helper) only exist when RegExp is emitted.
            if (_features.UsesRegExp)
            {
                var notRegExpLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldtoken, runtime.TSRegExpType);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
                il.Emit(OpCodes.Bne_Un, notRegExpLabel);
                il.Emit(OpCodes.Call, runtime.RegExpPrototypePopulateMethod);
                il.Emit(OpCodes.Ldsfld, runtime.RegExpPrototypeField);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(notRegExpLabel);
            }
            il.MarkLabel(notProtoNameLabel);

            var typePdsDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, typePdsDescLocal);
            il.Emit(OpCodes.Ldloc, typePdsDescLocal);
            var noTypePdsLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, noTypePdsLabel);
            il.Emit(OpCodes.Ldloc, typePdsDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(noTypePdsLabel);

            // Function.prototype.length for built-in constructor Type tokens.
            // CLR constructor overload counts are not JS arities, so identify
            // emitted intrinsics explicitly.  Error.constructor is Function,
            // hence both $Error and $TSFunction report length 1.
            var notTypeLengthLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "length");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, notTypeLengthLabel);
            var notErrorLengthLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, runtime.TSErrorType);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Bne_Un, notErrorLengthLabel);
            il.Emit(OpCodes.Ldc_R8, 1.0);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notErrorLengthLabel);
            var notFunctionLengthLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, runtime.TSFunctionType);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Bne_Un, notFunctionLengthLabel);
            il.Emit(OpCodes.Ldc_R8, 1.0);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notFunctionLengthLabel);
            il.MarkLabel(notTypeLengthLabel);

            // hasOwnProperty — return a $TSFunction wrapping HasOwnPropertyHelper,
            // bound to this Type as the receiver. Test262 patterns like
            // `Number.hasOwnProperty("prototype")` must dispatch through this.
            // Without this arm the lookup falls through to the .NET reflection
            // tail and finds nothing (or accidentally finds a CLR member by
            // case-insensitive matching).
            var notHasOwnLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "hasOwnProperty");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, notHasOwnLabel);
            il.Emit(OpCodes.Ldarg_0);
            _types.EmitLoadMethodInfo(il, runtime.HasOwnPropertyHelperMethod);
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notHasOwnLabel);

            // Cache the casted Type reference for the three reflection probes below.
            var typeLocal = il.DeclareLocal(_types.Type);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.Type);
            il.Emit(OpCodes.Stloc, typeLocal);

            const BindingFlags staticPublic = BindingFlags.Public | BindingFlags.Static;

            // Static method: SafeGetMethod(type, name, Public|Static).
            // SafeGetMethod handles AmbiguousMatchException deterministically, which matters
            // because user-declared statics can collide with inherited Type overloads.
            var staticMethodLocal = il.DeclareLocal(_types.MethodInfo);
            il.Emit(OpCodes.Ldloc, typeLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, (int)staticPublic);
            il.Emit(OpCodes.Call, runtime.SafeGetMethod);
            il.Emit(OpCodes.Stloc, staticMethodLocal);

            var noStaticMethodLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, staticMethodLocal);
            il.Emit(OpCodes.Brfalse, noStaticMethodLabel);

            // Found a static method — wrap in $TSFunction(null, methodInfo) so callers
            // invoking it through InvokeValue/InvokeMethodValue treat it as a callable.
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldloc, staticMethodLocal);
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(noStaticMethodLabel);

            // Static field: type.GetField(name, Public|Static).
            var staticFieldLocal = il.DeclareLocal(typeof(FieldInfo));
            il.Emit(OpCodes.Ldloc, typeLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, (int)staticPublic);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetField", _types.String, typeof(BindingFlags)));
            il.Emit(OpCodes.Stloc, staticFieldLocal);

            var noStaticFieldLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, staticFieldLocal);
            il.Emit(OpCodes.Brfalse, noStaticFieldLabel);

            // Found a static field — return field.GetValue(null).
            il.Emit(OpCodes.Ldloc, staticFieldLocal);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(typeof(FieldInfo), "GetValue", _types.Object));
            il.Emit(OpCodes.Ret);

            il.MarkLabel(noStaticFieldLabel);

            // Function.prototype.name: JS spec — classes expose their declared name as
            // `Foo.name === "Foo"`. Without this, `Class.name` falls through to Undefined
            // even though typeof(Class) === "function".
            var notClassNameLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "name");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, notClassNameLabel);
            il.Emit(OpCodes.Ldloc, typeLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "Name").GetGetMethod()!);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notClassNameLabel);

            // ECMA-262 §20.2.3: every Function instance inherits from
            // %Function.prototype%. Constructors (System.Type tokens) are
            // function objects, so `Error.constructor` / `String.constructor`
            // walks the proto chain to Function.prototype.constructor = Function
            // (= typeof($TSFunction)). Required for Test262 patterns like
            // `Function.prototype.isPrototypeOf(Error.constructor)`.
            var notTypeConstructorLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "constructor");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, notTypeConstructorLabel);
            il.Emit(OpCodes.Ldtoken, runtime.TSFunctionType);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notTypeConstructorLabel);

            // Built-in static-member dispatch (#63): for Type tokens that
            // represent a JS-level built-in constructor (Array → IList<object>,
            // Number → double, String → string), look up (type, name) against
            // the runtime table that mirrors the compile-time static emitters.
            // This is what makes `var A = Array; A.isArray(x)` work — the
            // compile-time ArrayStaticEmitter only runs for bare `Array.isArray`.
            var builtInLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldloc, typeLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.LookupBuiltInStaticMember);
            il.Emit(OpCodes.Stloc, builtInLocal);
            il.Emit(OpCodes.Ldloc, builtInLocal);
            var noBuiltInMatchLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, noBuiltInMatchLabel);
            il.Emit(OpCodes.Ldloc, builtInLocal);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(noBuiltInMatchLabel);

            // #265/#358: walk the constructor's superclass chain for inherited
            // statics. In Node a class constructor inherits from its parent
            // constructor (Object.getPrototypeOf(D) === C), so a static set on a
            // base — whether a string-keyed expando (`Base.foo = 1`) or a declared
            // static field/method (`static n = 7`) — is readable through a subclass
            // `D.foo` / `D.n`. Neither PDS (keyed by Type identity per-class with no
            // parent awareness) nor the own-only reflection probes above
            // (Public|Static finds no inherited statics without FlattenHierarchy)
            // crosses the chain, so probe each ancestor here. At every level the
            // order mirrors the subclass's own probes: PDS shadow first
            // (shadow-before-declared, so an expando write on an ancestor wins over
            // its declared field — #339 keeps nearer-subclass shadows out of this
            // walk by resolving them above), then declared static method, then
            // declared static field. Declared-member probes are gated on
            // $IHasFields so only emitted user classes are inspected — this skips
            // System.Object / runtime base types ($Array/$Promise/$Error) whose
            // BCL statics (e.g. Object.Equals) would otherwise bleed through.
            const BindingFlags declaredStaticPublic =
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var walkTypeLocal = il.DeclareLocal(_types.Type);
            var baseDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            var baseStaticMethodLocal = il.DeclareLocal(_types.MethodInfo);
            var baseStaticFieldLocal = il.DeclareLocal(typeof(FieldInfo));
            il.Emit(OpCodes.Ldloc, typeLocal);
            il.Emit(OpCodes.Stloc, walkTypeLocal);
            var baseWalkLoop = il.DefineLabel();
            il.MarkLabel(baseWalkLoop);
            // walkType = walkType.BaseType;  (null terminates the chain)
            il.Emit(OpCodes.Ldloc, walkTypeLocal);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "BaseType").GetGetMethod()!);
            il.Emit(OpCodes.Stloc, walkTypeLocal);
            il.Emit(OpCodes.Ldloc, walkTypeLocal);
            il.Emit(OpCodes.Brfalse, classInstanceLabel);
            // desc = PDSGetPropertyDescriptor(walkType, name);  (expando shadow)
            il.Emit(OpCodes.Ldloc, walkTypeLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, baseDescLocal);
            il.Emit(OpCodes.Ldloc, baseDescLocal);
            var baseProbeDeclaredLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, baseProbeDeclaredLabel);
            il.Emit(OpCodes.Ldloc, baseDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
            il.Emit(OpCodes.Ret);

            // No expando shadow on this ancestor — probe its declared statics, but
            // only when it is an emitted user class ($IHasFields.IsAssignableFrom).
            il.MarkLabel(baseProbeDeclaredLabel);
            il.Emit(OpCodes.Ldtoken, runtime.IHasFieldsInterface);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Ldloc, walkTypeLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "IsAssignableFrom", _types.Type));
            il.Emit(OpCodes.Brfalse, baseWalkLoop);

            // declared static method → $TSFunction(null, methodInfo)
            il.Emit(OpCodes.Ldloc, walkTypeLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, (int)declaredStaticPublic);
            il.Emit(OpCodes.Call, runtime.SafeGetMethod);
            il.Emit(OpCodes.Stloc, baseStaticMethodLocal);
            il.Emit(OpCodes.Ldloc, baseStaticMethodLocal);
            var noBaseStaticMethodLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, noBaseStaticMethodLabel);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldloc, baseStaticMethodLocal);
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(noBaseStaticMethodLabel);

            // declared static field → field.GetValue(null)
            il.Emit(OpCodes.Ldloc, walkTypeLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, (int)declaredStaticPublic);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetField", _types.String, typeof(BindingFlags)));
            il.Emit(OpCodes.Stloc, baseStaticFieldLocal);
            il.Emit(OpCodes.Ldloc, baseStaticFieldLocal);
            il.Emit(OpCodes.Brfalse, baseWalkLoop);
            il.Emit(OpCodes.Ldloc, baseStaticFieldLocal);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(typeof(FieldInfo), "GetValue", _types.Object));
            il.Emit(OpCodes.Ret);
        }

        // Callable wrapper handler: route .bind/.call/.apply/.length/.name through
        // GetFunctionMethod. Also handles .name specially for $BoundArrayMethod /
        // $BoundMapMethod / $BoundSetMethod by returning the captured method name,
        // which is more useful than GetFunctionMethod's empty-string fallback.
        il.MarkLabel(callableWrapperLabel);

        // Special-case "name" for bound methods — return the captured method name
        // (e.g. `map.get.name === 'get'`). GetFunctionMethod returns "" for unknown
        // callables, which is wrong for our wrappers.
        var notNameLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, _types.StringOpEquality);
        il.Emit(OpCodes.Brfalse, notNameLabel);

        var notBAMNameLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.BoundArrayMethodType);
        il.Emit(OpCodes.Brfalse, notBAMNameLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.BoundArrayMethodType);
        il.Emit(OpCodes.Ldfld, runtime.BoundArrayMethodNameField);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notBAMNameLabel);

        var notBMMNameLabel = il.DefineLabel();
        if (_features.UsesMap)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.BoundMapMethodType);
            il.Emit(OpCodes.Brfalse, notBMMNameLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.BoundMapMethodType);
            il.Emit(OpCodes.Ldfld, runtime.BoundMapMethodNameField);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notBMMNameLabel);
        }

        var notBSMNameLabel = il.DefineLabel();
        if (_features.UsesSet)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.BoundSetMethodType);
            il.Emit(OpCodes.Brfalse, notBSMNameLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.BoundSetMethodType);
            il.Emit(OpCodes.Ldfld, runtime.BoundSetMethodNameField);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notBSMNameLabel);
        }

        // $BoundAnyFunction has no name field — fall through to GetFunctionMethod (returns "")

        il.MarkLabel(notNameLabel);

        // All other names (bind/call/apply/length/anything else) — delegate to GetFunctionMethod
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetFunctionMethod);
        il.Emit(OpCodes.Ret);

        // TypedArray family handlers — gated together with the dispatch arms
        // above. When typed arrays aren't referenced, none of these labels are
        // branched to, so we skip the entire body.
        if (_features.HasAnyTypedArray)
        {
            // TypedArray handler - call emitted typed-array member helper
            il.MarkLabel(typedArrayLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.GetTypedArrayMemberMethod);
            il.Emit(OpCodes.Ret);

            // $ArrayBuffer handler - check for "byteLength"
            il.MarkLabel(arrayBufferLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "byteLength");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            var notArrayBufferByteLengthLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, notArrayBufferByteLengthLabel);
            // Return ByteLength as double
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.ArrayBufferType);
            il.Emit(OpCodes.Callvirt, runtime.ArrayBufferByteLengthGetter);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notArrayBufferByteLengthLabel);
            // Return null for other properties
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);

            // $SharedArrayBuffer handler - check for "byteLength"
            il.MarkLabel(sharedArrayBufferLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "byteLength");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            var notSharedArrayBufferByteLengthLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, notSharedArrayBufferByteLengthLabel);
            // Return ByteLength as double
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.SharedArrayBufferType);
            il.Emit(OpCodes.Callvirt, runtime.SharedArrayBufferByteLengthGetter);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notSharedArrayBufferByteLengthLabel);
            // Return null for other properties
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);

            EmitDataViewHandler();
        }

        // $DataView handler local helper — extracted so the enclosing
        // `if (_features.HasAnyTypedArray)` block can call it once.
        void EmitDataViewHandler()
        {
            // $DataView handler - check for "byteLength", "byteOffset", "buffer"
            il.MarkLabel(dataViewLabel);
        // Check "byteLength"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "byteLength");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notDataViewByteLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notDataViewByteLengthLabel);
        // Return ByteLength as double
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.DataViewType);
        il.Emit(OpCodes.Callvirt, runtime.DataViewByteLengthGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notDataViewByteLengthLabel);
        // Check "byteOffset"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "byteOffset");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notDataViewByteOffsetLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notDataViewByteOffsetLabel);
        // Return ByteOffset as double
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.DataViewType);
        il.Emit(OpCodes.Callvirt, runtime.DataViewByteOffsetGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notDataViewByteOffsetLabel);
        // Check "buffer"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "buffer");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notDataViewBufferLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notDataViewBufferLabel);
        // Return Buffer
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.DataViewType);
        il.Emit(OpCodes.Callvirt, runtime.DataViewBufferGetter);
        il.Emit(OpCodes.Ret);
            il.MarkLabel(notDataViewBufferLabel);
            // Return null for other properties
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // Promise (Task<object?> or $Promise) handler - return TSFunction wrappers for then/catch/finally
        il.MarkLabel(promiseLabel);

        // Promise instances are ordinary extensible objects.  An own data or
        // accessor descriptor must shadow Promise.prototype (notably `then`,
        // whose lookup is observable from catch/finally).  Raw intrinsic
        // promises are Task<object?> values, so they cannot expose expandos as
        // CLR fields and use the descriptor store instead.
        var promisePdsDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, promisePdsDescLocal);
        var noPromisePdsDescLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, promisePdsDescLocal);
        il.Emit(OpCodes.Brfalse, noPromisePdsDescLabel);
        var promiseDataDescLabel = il.DefineLabel();
        var promiseGetterLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, promisePdsDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, promiseGetterLocal);
        il.Emit(OpCodes.Ldloc, promiseGetterLocal);
        il.Emit(OpCodes.Brfalse, promiseDataDescLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, promiseGetterLocal);
        il.Emit(OpCodes.Call, EmitGenerics.MakeGenericMethod(
            _types.GetMethod(typeof(System.Array), "Empty"), _types.Object));
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(promiseDataDescLabel);
        il.Emit(OpCodes.Ldloc, promisePdsDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(noPromisePdsDescLabel);

        // First, extract the underlying Task if this is a $Promise object
        // Store the task in a local variable for use in creating TSFunction wrappers
        var taskLocal = il.DeclareLocal(_types.TaskOfObject);
        var isTSPromiseLabel = il.DefineLabel();
        var haveTaskLabel = il.DefineLabel();

        // Check if obj is $Promise
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brtrue, isTSPromiseLabel);

        // It's a raw Task<object?>, use directly
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.TaskOfObject);
        il.Emit(OpCodes.Stloc, taskLocal);
        il.Emit(OpCodes.Br, haveTaskLabel);

        // It's a $Promise, extract the Task property
        il.MarkLabel(isTSPromiseLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSPromiseType);
        il.Emit(OpCodes.Callvirt, runtime.TSPromiseTaskGetter);
        il.Emit(OpCodes.Stloc, taskLocal);

        il.MarkLabel(haveTaskLabel);

        // then/catch/finally: walk Promise.prototype dict so the wrappers
        // returned are identical to those on Promise.prototype itself
        // (`p.then === Promise.prototype.then`, spec-correct .length/.name
        // derived from the __this-aware helper signatures). The previous
        // implementation constructed bound $TSFunction wrappers per access
        // — which broke identity AND reported length=3 / name="PromiseThen"
        // for `p.then`. The PromiseThenHelper/PromiseCatchHelper/
        // PromiseFinallyHelper installed in PromisePrototypePopulate accept
        // `__this` so chaining via `.call(p, fn)` works without needing
        // task pre-binding.
        var taskLocal2 = taskLocal; // silence unused-warning; kept for legacy
        void EmitPromiseProtoLookup(string jsName)
        {
            var notThisLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, notThisLabel);
            il.Emit(OpCodes.Call, runtime.PromisePrototypePopulateMethod);
            var protoValLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldsfld, runtime.PromisePrototypeField);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Ldloca, protoValLocal);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
                _types.String, _types.Object.MakeByRefType()));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldloc, protoValLocal);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notThisLabel);
        }
        EmitPromiseProtoLookup("then");
        EmitPromiseProtoLookup("catch");
        EmitPromiseProtoLookup("finally");

        // ECMA-262 §27.2.5.1: Promise.prototype.constructor is %Promise%.
        // Bare `Promise` resolves to typeof(Task<object?>) in compiled mode
        // (TryEmitBuiltInClassType / GlobalThisGetProperty), so return the
        // same Type token here for identity:
        // `Promise.resolve(1).constructor === Promise` (#221 increment).
        // #242 subclass instances ($Promise-derived emitted classes) return
        // their own class token instead, so `MyP.resolve(1).constructor === MyP`.
        // Generic subclasses carry constructed types (MyP<object>) while bare
        // `MyP` emits the open definition — return the definition (same gap
        // InstanceOf's generic-definition walk handles).
        var notPromiseCtorLabel = il.DefineLabel();
        var defaultPromiseCtorLabel = il.DefineLabel();
        var nonGenericCtorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notPromiseCtorLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brfalse, defaultPromiseCtorLabel);
        var ctorTypeLocal = il.DeclareLocal(_types.Type);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, ctorTypeLocal);
        il.Emit(OpCodes.Ldloc, ctorTypeLocal);
        il.Emit(OpCodes.Ldtoken, runtime.TSPromiseType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Beq, defaultPromiseCtorLabel);
        il.Emit(OpCodes.Ldloc, ctorTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Type, "IsGenericType")!.GetGetMethod()!);
        il.Emit(OpCodes.Brfalse, nonGenericCtorLabel);
        il.Emit(OpCodes.Ldloc, ctorTypeLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Type, "GetGenericTypeDefinition"));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(nonGenericCtorLabel);
        il.Emit(OpCodes.Ldloc, ctorTypeLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(defaultPromiseCtorLabel);
        il.Emit(OpCodes.Ldtoken, _types.TaskOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notPromiseCtorLabel);

        // Unknown promise property - return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

    }

    /// <summary>
    /// Phase 1: Define $MethodCallable type (wraps BuiltInMethod or other callable objects
    /// returned by GetMember so they can be dispatched through InvokeMethodValue/InvokeValue).
    /// </summary>
    internal void EmitMethodCallableTypeDefinition(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = EmitTypeDefinitions.DefineType(moduleBuilder,
            "$MethodCallable",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.Object
        );
        runtime.MethodCallableType = typeBuilder;

        // Field: object _callable
        var callableField = typeBuilder.DefineField("_callable", _types.Object, FieldAttributes.Private);
        runtime.MethodCallableField = callableField;

        // Constructor: $MethodCallable(object callable)
        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Object]
        );
        runtime.MethodCallableCtor = ctorBuilder;

        var ctorIL = ctorBuilder.GetILGenerator();
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
        ctorIL.Emit(OpCodes.Ldarg_0);
        ctorIL.Emit(OpCodes.Ldarg_1);
        ctorIL.Emit(OpCodes.Stfld, callableField);
        ctorIL.Emit(OpCodes.Ret);

        // Define Invoke method signature (body emitted in Phase 2)
        var invokeBuilder = typeBuilder.DefineMethod(
            "Invoke",
            MethodAttributes.Public,
            _types.Object,
            [_types.ObjectArray]
        );
        runtime.MethodCallableInvoke = invokeBuilder;
    }

    /// <summary>
    /// Phase 2: Emit Invoke method body for $MethodCallable and create the type.
    /// Uses reflection to call "Invoke" (for TSFunction) or "Call" (for BuiltInMethod) on the wrapped object.
    /// </summary>
    internal void EmitMethodCallableFinalize(EmittedRuntime runtime)
    {
        var callableField = runtime.MethodCallableField;
        var invokeBuilder = runtime.MethodCallableInvoke;

        var il = invokeBuilder.GetILGenerator();

        // Locals
        var callableLocal = il.DeclareLocal(_types.Object);         // 0: _callable
        var typeLocal = il.DeclareLocal(typeof(Type));               // 1: callable.GetType()
        var methodLocal = il.DeclareLocal(_types.MethodInfo);        // 2: MethodInfo

        // Load _callable
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, callableField);
        il.Emit(OpCodes.Stloc, callableLocal);

        // Get type
        il.Emit(OpCodes.Ldloc, callableLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Stloc, typeLocal);

        // Try "Invoke" method first (TSFunction, Func<>, etc.)
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "Invoke");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Stloc, methodLocal);

        var noInvokeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Brfalse, noInvokeLabel);

        // Found "Invoke" - call: methodInfo.Invoke(_callable, new object[] { args })
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Ldloc, callableLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1); // args (object[])
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Ret);

        // Try "Call" method (BuiltInMethod.Call(Interpreter, List<object?>))
        il.MarkLabel(noInvokeLabel);
        il.Emit(OpCodes.Ldloc, typeLocal);
        il.Emit(OpCodes.Ldstr, "Call");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetMethod", _types.String));
        il.Emit(OpCodes.Stloc, methodLocal);

        var noCallLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Brfalse, noCallLabel);

        // Found "Call" - call: methodInfo.Invoke(_callable, new object[] { null, new List<object?>(args) })
        // null interpreter is an established pattern (SharpTSEventEmitter.InvokeListenerDirect)
        il.Emit(OpCodes.Ldloc, methodLocal);
        il.Emit(OpCodes.Ldloc, callableLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        // args[0] = null (interpreter)
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stelem_Ref);
        // args[1] = new List<object?>(args)
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_1); // args (object[])
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, [typeof(IEnumerable<object>)])!);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Ret);

        // No callable method found - return null
        il.MarkLabel(noCallLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        runtime.MethodCallableType.CreateType();
    }
}

