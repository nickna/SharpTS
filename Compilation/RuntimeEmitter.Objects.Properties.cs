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
        var methodsArrayType = _types.MethodInfo.MakeArrayType();
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
        // (String is handled inline in EmitGetProperty's stringLabel branch.)
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

        // Check $Error - handle name, message, stack properties
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

        il.MarkLabel(noGetMemberLabel);
        il.MarkLabel(nullLabel);
        il.MarkLabel(skipTypeReflectionLabel);
        // Return $Undefined.Instance for non-existent properties (JavaScript semantics)
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSetFieldsProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // SetFieldsProperty(object obj, string name, object value) -> void
        // Updates class-instance state through emitted runtime state only:
        // descriptor store checks and emitted $Object fields.
        var method = typeBuilder.DefineMethod(
            "SetFieldsProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.String, _types.Object]
        );
        runtime.SetFieldsProperty = method;

        var il = method.GetILGenerator();
        var endLabel = il.DefineLabel();
        var tryFieldsLabel = il.DefineLabel();
        var trySetterLabel = il.DefineLabel();

        // Declare locals upfront
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var frozenCheckLocal = il.DeclareLocal(_types.Object);

        // if (obj == null) return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Check if frozen: _frozenObjects.TryGetValue(obj, out _)
        // If frozen, silently return (non-strict mode behavior)
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, frozenCheckLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brtrue, endLabel); // Frozen - silently return

        // No additional setter fallback in standalone mode.
        il.MarkLabel(trySetterLabel);

        // Try _fields dictionary - walk up type hierarchy to find non-null _fields
        il.MarkLabel(tryFieldsLabel);
        var notTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObjectLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectFieldsGetter);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Found a non-null _fields dictionary
        // Check if sealed: _sealedObjects.TryGetValue(obj, out _)
        var doSetFieldLabel = il.DefineLabel();
        var checkExtensibilityLabel = il.DefineLabel();
        var sealedCheckLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, sealedCheckLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, checkExtensibilityLabel); // Not sealed, check extensibility

        // Object is sealed - check if property exists in dictionary
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_1); // name
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Brfalse, endLabel); // Property doesn't exist on sealed object, silently return
        il.Emit(OpCodes.Br, doSetFieldLabel); // Property exists, proceed to set

        // Not sealed - check extensibility via $PropertyDescriptorStore - fully standalone, no reflection
        il.MarkLabel(checkExtensibilityLabel);
        il.Emit(OpCodes.Ldarg_0); // obj
        il.Emit(OpCodes.Ldarg_1); // name
        il.Emit(OpCodes.Call, runtime.PDSCanAddProperty);
        il.Emit(OpCodes.Brfalse, endLabel); // Cannot add property, silently return

        // Set the value: dict[name] = value;
        il.MarkLabel(doSetFieldLabel);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notTSObjectLabel);

        // Check $IHasFields interface (covers user-defined classes)
        var notHasFieldsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, notHasFieldsLabel);

        // Check extensibility before setting (handles sealed/non-extensible objects)
        il.Emit(OpCodes.Ldarg_0); // obj
        il.Emit(OpCodes.Ldarg_1); // name
        il.Emit(OpCodes.Call, runtime.PDSCanAddProperty);
        il.Emit(OpCodes.Brfalse, endLabel); // Cannot add property, silently return

        // Call interface method: ((IHasFields)obj).SetProperty(name, value)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsSetProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notHasFieldsLabel);

        // Check $Error - handle name, message, stack properties
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
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorNameSetter);
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
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorMessageSetter);
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
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorStackSetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notErrorStackLabel);

        // Check "code"
        var notErrorCodeSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "code");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notErrorCodeSetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorCodeSetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notErrorCodeSetLabel);

        // Check "syscall"
        var notErrorSyscallSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "syscall");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notErrorSyscallSetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorSyscallSetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notErrorSyscallSetLabel);

        il.MarkLabel(notErrorLabel);

        // Scoped PDS-data-store fallback for ECMA built-ins: $TSDate, $TSRegExp,
        // $TSPromise, $TSError. JS allows ad-hoc property assignment on these
        // instances (`d = new Date(); d.foo = 1; d.foo === 1`); the value lands
        // in PDS so GetFieldsProperty's PDS-data-descriptor arm reads it back.
        // Limited to these types so user-defined class instances and runtime-
        // side types (which may rely on silent-no-op semantics for unknown
        // writes — e.g., the Debug npm package) are not affected.
        // Note: $TSError already has explicit name/message/stack/code/syscall
        // handlers above and only reaches the PDS path for OTHER property
        // names (like `obj.length`, `obj[0]`).
        var pdsStoreLabel = il.DefineLabel();
        var afterPdsStoreLabel = il.DefineLabel();
        if (_features.UsesDate)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSDateType);
            il.Emit(OpCodes.Brtrue, pdsStoreLabel);
        }
        if (_features.UsesRegExp)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brtrue, pdsStoreLabel);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brtrue, pdsStoreLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSErrorType);
        il.Emit(OpCodes.Brtrue, pdsStoreLabel);
        il.Emit(OpCodes.Br, afterPdsStoreLabel);

        il.MarkLabel(pdsStoreLabel);
        {
            var fbDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
            il.Emit(OpCodes.Stloc, fbDescLocal);
            il.Emit(OpCodes.Ldloc, fbDescLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, fbDescLocal);
            il.Emit(OpCodes.Call, runtime.PDSDefineProperty);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(afterPdsStoreLabel);

        // Fallback: Try SetMember(string, object) method for types like $HttpResponse
        // that expose property setters through their SetMember dispatch method.
        var setMemberLocal = il.DeclareLocal(_types.MethodInfo);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "SetMember");
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public));
        il.Emit(OpCodes.Call, runtime.SafeGetMethod);
        il.Emit(OpCodes.Stloc, setMemberLocal);

        il.Emit(OpCodes.Ldloc, setMemberLocal);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Call SetMember(name, value): methodInfo.Invoke(obj, new object[] { name, value })
        il.Emit(OpCodes.Ldloc, setMemberLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1); // name
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_2); // value
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Pop);

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits SetFieldsPropertyStrict(object obj, string name, object value, bool strictMode) -> void
    /// In strict mode, throws TypeError for modifications to frozen objects.
    /// </summary>
    private void EmitSetFieldsPropertyStrict(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetFieldsPropertyStrict",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.String, _types.Object, _types.Boolean]
        );
        runtime.SetFieldsPropertyStrict = method;

        var il = method.GetILGenerator();
        var endLabel = il.DefineLabel();
        var notFrozenLabel = il.DefineLabel();
        var tryFieldsLabel = il.DefineLabel();

        // Declare locals upfront
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var frozenCheckLocal = il.DeclareLocal(_types.Object);

        // if (obj == null) return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Check if frozen: _frozenObjects.TryGetValue(obj, out _)
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, frozenCheckLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, notFrozenLabel);

        // Frozen - check strictMode and throw if true
        il.Emit(OpCodes.Ldarg_3); // strictMode
        var frozenSilentLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, frozenSilentLabel);
        EmitThrowTypeErrorWithName(il, runtime, "Cannot assign to read only property '", "' of object");
        il.MarkLabel(frozenSilentLabel);
        il.Emit(OpCodes.Ret); // Silently return in non-strict mode

        il.MarkLabel(notFrozenLabel);

        // No reflection setter fallback in standalone mode.

        // Try _fields dictionary - walk up type hierarchy to find non-null _fields
        il.MarkLabel(tryFieldsLabel);

        var notTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brfalse, notTSObjectLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectFieldsGetter);
        il.Emit(OpCodes.Stloc, dictLocal);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Found a non-null _fields dictionary - set the value and return
        // dict[name] = value;
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTSObjectLabel);

        // Check $IHasFields interface (covers user-defined classes)
        var notHasFieldsLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Brfalse, notHasFieldsLabel);

        // Check extensibility before setting (handles sealed/non-extensible objects)
        il.Emit(OpCodes.Ldarg_0); // obj
        il.Emit(OpCodes.Ldarg_1); // name
        il.Emit(OpCodes.Call, runtime.PDSCanAddProperty);
        il.Emit(OpCodes.Brfalse, endLabel); // Cannot add property, silently return

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.IHasFieldsInterface);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.IHasFieldsSetProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notHasFieldsLabel);

        // Check $Error - handle name, message, stack properties
        var notErrorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSErrorType);
        il.Emit(OpCodes.Brfalse, notErrorLabel);

        var notErrorNameLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "name");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notErrorNameLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorNameSetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notErrorNameLabel);

        var notErrorMessageLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "message");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notErrorMessageLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorMessageSetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notErrorMessageLabel);

        var notErrorStackLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "stack");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notErrorStackLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSErrorType);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, runtime.TSErrorStackSetter);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notErrorStackLabel);

        il.MarkLabel(notErrorLabel);

        // Built-in exotic objects (Date / RegExp / Promise / Error) accept arbitrary
        // named property assignment per ECMA-262 [[Set]], stored as PDS data
        // descriptors so GetProperty / for-in / gOPD round-trip. Mirrors the
        // non-strict SetFieldsProperty PDS-store arm — without it these writes were
        // silently dropped under "use strict", so e.g. `var d = new Date(0);
        // d.enumerable = true;` (a defineProperty attributes object — Test262
        // Object/defineProperty 15.2.3.6-3-39 et al.) lost the field. Frozen /
        // extensibility throws were already handled at the top of this method.
        var fieldsPdsStoreLabel = il.DefineLabel();
        var afterFieldsPdsStoreLabel = il.DefineLabel();
        if (_features.UsesDate)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSDateType);
            il.Emit(OpCodes.Brtrue, fieldsPdsStoreLabel);
        }
        if (_features.UsesRegExp)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brtrue, fieldsPdsStoreLabel);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSPromiseType);
        il.Emit(OpCodes.Brtrue, fieldsPdsStoreLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSErrorType);
        il.Emit(OpCodes.Brtrue, fieldsPdsStoreLabel);
        il.Emit(OpCodes.Br, afterFieldsPdsStoreLabel);

        il.MarkLabel(fieldsPdsStoreLabel);
        {
            var fbDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Newobj, runtime.CompiledPropertyDescriptorCtor);
            il.Emit(OpCodes.Stloc, fbDescLocal);
            il.Emit(OpCodes.Ldloc, fbDescLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, fbDescLocal);
            il.Emit(OpCodes.Call, runtime.PDSDefineProperty);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(afterFieldsPdsStoreLabel);

        // Fallback: Try SetMember(string, object) method for types like $HttpResponse
        var setMemberLocal = il.DeclareLocal(_types.MethodInfo);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "GetType"));
        il.Emit(OpCodes.Ldstr, "SetMember");
        il.Emit(OpCodes.Ldc_I4, (int)(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public));
        il.Emit(OpCodes.Call, runtime.SafeGetMethod);
        il.Emit(OpCodes.Stloc, setMemberLocal);

        il.Emit(OpCodes.Ldloc, setMemberLocal);
        il.Emit(OpCodes.Brfalse, endLabel);

        // Call SetMember(name, value): methodInfo.Invoke(obj, new object[] { name, value })
        il.Emit(OpCodes.Ldloc, setMemberLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_1); // name
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_2); // value
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.MethodInfo, "Invoke", _types.Object, _types.ObjectArray));
        il.Emit(OpCodes.Pop);

        il.MarkLabel(endLabel);
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
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue",
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
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue",
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
        var namespaceLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();

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
        EmitProxyGetPropertyCheck(il, () => il.Emit(OpCodes.Ldarg_0), () => il.Emit(OpCodes.Ldarg_1), notProxyLabel);

        il.MarkLabel(notProxyLabel);

        // $TSNamespace - call ns.Get(name)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSNamespaceType);
        il.Emit(OpCodes.Brtrue, namespaceLabel);

        // $Object (with getter/setter support) - call obj.GetProperty(name)
        var tsObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, tsObjectLabel);

        // Map (Dictionary<object, object>) - check for "size" property.
        // Gated together with the handler body below.
        var mapLabel = il.DefineLabel();
        if (_features.UsesMap)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, _types.DictionaryObjectObject);
            il.Emit(OpCodes.Brtrue, mapLabel);
        }

        // Set (HashSet<object>) - duck-typed access via GetSetProperty.
        var setLabel = il.DefineLabel();
        if (_features.UsesSet)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, _types.HashSetOfObject);
            il.Emit(OpCodes.Brtrue, setLabel);
        }

        // Dictionary (regular object)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

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

        // $Array - check for "length" (inherits List<object?>; MUST come
        // BEFORE the plain List check so sparse-aware length is used —
        // otherwise `new Array(10_000_000).length` returns 0).
        var sharpTSArrayLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, sharpTSArrayLabel);

        // List - check for "length"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listLabel);

        // object[] (compiled `arguments` representation) - check for "length"
        var objectArrayLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ObjectArray);
        il.Emit(OpCodes.Brtrue, objectArrayLabel);

        // String - check for "length"
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        // $Buffer - check for "length" and "toString". Only meaningful when
        // some feature emitted $Buffer (crypto/fs/zlib/http/fetch/dgram/net).
        var tsBufferLabel = il.DefineLabel();
        if (_features.UsesBuffer)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSBufferType);
            il.Emit(OpCodes.Brtrue, tsBufferLabel);
        }

        // $Stats - check for isFile, isDirectory, size, etc. Only when fs is on.
        var tsStatsLabel = il.DefineLabel();
        if (_features.UsesFs)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.StatsType);
            il.Emit(OpCodes.Brtrue, tsStatsLabel);
        }

        // $TSFunction - check for bind/call/apply
        var tsFunctionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, tsFunctionLabel);

        // $BoundTSFunction - also check for bind/call/apply
        var boundFunctionLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.BoundTSFunctionType);
        il.Emit(OpCodes.Brtrue, boundFunctionLabel);

        // $CJSModule - route to the module's GetMember(name) for exports/id/filename/etc.
        // Only emitted when the program uses CommonJS (require/module/exports).
        var cjsModuleGetLabel = il.DefineLabel();
        if (_features.UsesCjsRequire)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.CjsModuleType);
            il.Emit(OpCodes.Brtrue, cjsModuleGetLabel);
        }

        // $RegExp — surface the built-in slots (`lastIndex`, `source`, `flags`,
        // `global`, `ignoreCase`, `multiline`, `sticky`, `unicode`, `hasIndices`,
        // `dotAll`, `unicodeSets`) via the typed getters / parsed-from-flags
        // expressions. Without this branch the read falls through to
        // GetFieldsProperty whose reflection lookup is case-sensitive ("lastIndex"
        // vs "LastIndex") and silently returns undefined. Test262's
        // builtin-coerce-lastindex.js + many coerce/builtin-* tests require the
        // internal slot value to round-trip through `r.lastIndex` reads/writes.
        var tsRegExpGetLabel = il.DefineLabel();
        if (_features.UsesRegExp)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brtrue, tsRegExpGetLabel);
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
            il.Emit(OpCodes.Ldtoken, runtime.HasOwnPropertyHelperMethod);
            il.Emit(OpCodes.Ldtoken, runtime.HasOwnPropertyHelperMethod.DeclaringType!);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle",
                _types.RuntimeMethodHandle, _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Castclass, _types.MethodInfo);
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

        // Namespace handler - call ns.Get(name)
        il.MarkLabel(namespaceLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSNamespaceType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSNamespaceGet);
        il.Emit(OpCodes.Ret);

        // $Object handler - call obj.GetProperty(name) which handles getters.
        // If the property is missing (HasProperty=false) and there's no own PDS
        // descriptor either, walk the prototype chain via PDSGetPrototype.
        // Required for Test262 `Con.prototype = proto; new Con(); obj.length`
        // patterns where length lives on the prototype, and conversely
        // `Object.defineProperty(child, "length", {value:2})` (PDS-only) must
        // override an inherited accessor on proto.
        il.MarkLabel(tsObjectLabel);
        // Object.prototype method short-circuits (Stage 4z15 follow-on):
        // expose hasOwnProperty + isPrototypeOf as $TSFunction wrappers
        // bound to this $Object.
        void EmitTSObjProtoCheck(string jsName, MethodBuilder helper)
        {
            var skip = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, skip);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, helper);
            il.Emit(OpCodes.Ldtoken, helper.DeclaringType!);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle",
                _types.RuntimeMethodHandle, _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Castclass, _types.MethodInfo);
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(skip);
        }
        EmitTSObjProtoCheck("hasOwnProperty", runtime.HasOwnPropertyHelperMethod);
        EmitTSObjProtoCheck("isPrototypeOf",  runtime.IsPrototypeOfHelperMethod);

        var tsObjectInstanceLocal = il.DeclareLocal(runtime.TSObjectType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Stloc, tsObjectInstanceLocal);
        // if (obj.HasProperty(name)) return obj.GetProperty(name)
        il.Emit(OpCodes.Ldloc, tsObjectInstanceLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectHasProperty);
        var tsObjectCheckPDS = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, tsObjectCheckPDS);
        il.Emit(OpCodes.Ldloc, tsObjectInstanceLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectGetProperty);
        il.Emit(OpCodes.Ret);
        // Check PDS for own data descriptor / accessor before walking chain
        il.MarkLabel(tsObjectCheckPDS);
        var tsObjectPDSDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldloc, tsObjectInstanceLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, tsObjectPDSDescLocal);
        var tsObjectWalkProto = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tsObjectPDSDescLocal);
        il.Emit(OpCodes.Brfalse, tsObjectWalkProto);
        // Has own descriptor: getter wins, else return value
        var tsObjectPDSValue = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tsObjectPDSDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Brfalse, tsObjectPDSValue);
        // Invoke getter via InvokeMethodValue(obj, getter, [])
        var tsObjectGetterLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, tsObjectGetterLocal);
        il.Emit(OpCodes.Ldloc, tsObjectInstanceLocal);
        il.Emit(OpCodes.Ldloc, tsObjectGetterLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsObjectPDSValue);
        il.Emit(OpCodes.Pop); // discard the null getter
        il.Emit(OpCodes.Ldloc, tsObjectPDSDescLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
        il.Emit(OpCodes.Ret);
        // Walk prototype chain
        il.MarkLabel(tsObjectWalkProto);
        var tsObjectProtoLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldloc, tsObjectInstanceLocal);
        il.Emit(OpCodes.Call, runtime.PDSGetPrototype);
        il.Emit(OpCodes.Stloc, tsObjectProtoLocal);
        var tsObjectNoProto = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, tsObjectProtoLocal);
        il.Emit(OpCodes.Brfalse, tsObjectNoProto);
        // Recursively call GetProperty(prototype, name)
        il.Emit(OpCodes.Ldloc, tsObjectProtoLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsObjectNoProto);
        // No own prototype — fall back to Object.prototype singleton (mirrors
        // the dict-branch fallback). Catches `({}.toString)` style accesses on
        // $Object instances created without an explicit prototype link.
        var tsObjProtoFallbackMissLabel = il.DefineLabel();
        il.Emit(OpCodes.Call, runtime.ObjectPrototypePopulateMethod);
        var tsObjProtoFallbackLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, tsObjProtoFallbackLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, tsObjProtoFallbackMissLabel);
        il.Emit(OpCodes.Ldloc, tsObjProtoFallbackLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsObjProtoFallbackMissLabel);
        // Property absent on object and Object.prototype — return undefined.
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        // $TSFunction handler - call GetFunctionMethod(func, name)
        il.MarkLabel(tsFunctionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetFunctionMethod);
        il.Emit(OpCodes.Ret);

        // $BoundTSFunction handler - call GetFunctionMethod(func, name)
        il.MarkLabel(boundFunctionLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetFunctionMethod);
        il.Emit(OpCodes.Ret);

        // $CJSModule handler - call module.GetMember(name) — only emitted when
        // UsesCjsRequire is on (matching the dispatch arm above).
        if (_features.UsesCjsRequire)
        {
            il.MarkLabel(cjsModuleGetLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.CjsModuleType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Callvirt, runtime.CjsModuleType.GetMethod("GetMember", [_types.String])!);
            il.Emit(OpCodes.Ret);
        }

        // $RegExp handler — surface built-in slots via typed getters, plus
        // flag-string-parsed accessors (sticky/unicode/hasIndices/dotAll/
        // unicodeSets) for the JS-spec flags that don't have dedicated
        // .NET-side fields. Unknown property names fall through to
        // GetFieldsProperty so user-set data (`r.foo = 1`, descriptor-
        // installed properties) still resolves correctly.
        if (_features.UsesRegExp)
        {
            il.MarkLabel(tsRegExpGetLabel);
            var rxLocal = il.DeclareLocal(runtime.TSRegExpType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSRegExpType);
            il.Emit(OpCodes.Stloc, rxLocal);

            // ECMA-262 §22.2.6.* read paths go through ordinary Get, so
            // user-installed Object.defineProperty(r, 'flags', {get}) etc.
            // must win over the internal slot. Check PDS first for any name;
            // when a descriptor is present, surface its value (data) or
            // invoke its getter (accessor) before reaching the typed slot
            // fast-paths below. Symbol.match's get-flags-err.js,
            // builtin-coerce-lastindex.js and friends rely on the override
            // path running before the internal-slot read.
            var pdsDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, pdsDescLocal);
            var noPdsDescLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, pdsDescLocal);
            il.Emit(OpCodes.Brfalse, noPdsDescLabel);
            // Accessor descriptor? Getter != null → invoke fn(thisArg=rx).
            var dataDescLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, pdsDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
            var regexpGetterLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Stloc, regexpGetterLocal);
            il.Emit(OpCodes.Ldloc, regexpGetterLocal);
            il.Emit(OpCodes.Brfalse, dataDescLabel);
            // Cast getter to $TSFunction and InvokeWithThis(rx). If the
            // descriptor's getter slot isn't a $TSFunction (shouldn't
            // happen normally), fall through to the data path.
            var regexpFnLocal = il.DeclareLocal(runtime.TSFunctionType);
            il.Emit(OpCodes.Ldloc, regexpGetterLocal);
            il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
            il.Emit(OpCodes.Stloc, regexpFnLocal);
            il.Emit(OpCodes.Ldloc, regexpFnLocal);
            il.Emit(OpCodes.Brfalse, dataDescLabel);
            il.Emit(OpCodes.Ldloc, regexpFnLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetMethod(typeof(System.Array), "Empty").MakeGenericMethod(_types.Object));
            il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(dataDescLabel);
            // Data descriptor — return descriptor.Value.
            il.Emit(OpCodes.Ldloc, pdsDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(noPdsDescLabel);

            // Helper closure to emit a name-equality test + branch to a
            // labelled body. Keeps the dispatch table readable.
            void NameMatchBranch(string propName, System.Action emitBody)
            {
                var notThisName = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, propName);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, notThisName);
                emitBody();
                il.Emit(OpCodes.Ret);
                il.MarkLabel(notThisName);
            }

            // "lastIndex" — return the raw boxed value when a non-numeric value
            // was assigned (object identity preserved per spec); otherwise the
            // typed int as a boxed double.
            NameMatchBranch("lastIndex", () =>
            {
                var numericLabel = il.DefineLabel();
                var doneLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, rxLocal);
                il.Emit(OpCodes.Ldfld, _tsRegExpLastIndexBoxedField);
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Brfalse, numericLabel);
                il.Emit(OpCodes.Br, doneLabel);            // boxed non-null → return it
                il.MarkLabel(numericLabel);
                il.Emit(OpCodes.Pop);                      // drop the null
                il.Emit(OpCodes.Ldloc, rxLocal);
                il.Emit(OpCodes.Callvirt, runtime.TSRegExpLastIndexGetter);
                il.Emit(OpCodes.Conv_R8);
                il.Emit(OpCodes.Box, _types.Double);
                il.MarkLabel(doneLabel);
            });
            // "constructor" — inherited from RegExp.prototype.constructor, which
            // is the RegExp constructor (the $RegExp Type token, == what `RegExp`
            // evaluates to as a value). Without this an instance read returns
            // undefined and `re.constructor === RegExp` is false, blocking the
            // §22.2.4.1 call-form same-object check. PDS is checked above, so a
            // user `Object.defineProperty(re,'constructor',…)` / `re.constructor=x`
            // still wins.
            NameMatchBranch("constructor", () =>
            {
                il.Emit(OpCodes.Ldtoken, runtime.TSRegExpType);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
            });
            // "source" / "flags" — string fields.
            NameMatchBranch("source", () =>
            {
                il.Emit(OpCodes.Ldloc, rxLocal);
                il.Emit(OpCodes.Callvirt, runtime.TSRegExpSourceGetter);
            });
            // Spec-aligned ECMA-262 §22.2.6.4 — assemble the flags string from
            // individual property reads so user-installed `Object.defineProperty
            // (r, 'global', {get})` overrides participate in the chain. Each
            // Get(rx, propName) goes through this very GetProperty recursively,
            // so PDS-first lookup on the per-flag property fires first; the
            // typed slot fallback returns the same boxed bool we'd have read
            // directly, keeping the assembled string identical to _flags for
            // ordinary $RegExp without overrides. Unlocks Symbol.match/replace/
            // split/search's get-global-err / coerce-global / get-unicode-error
            // test262 family.
            NameMatchBranch("flags", () =>
            {
                var sbLocal = il.DeclareLocal(typeof(System.Text.StringBuilder));
                il.Emit(OpCodes.Newobj, typeof(System.Text.StringBuilder).GetConstructor(Type.EmptyTypes)!);
                il.Emit(OpCodes.Stloc, sbLocal);

                var sbAppendChar = typeof(System.Text.StringBuilder).GetMethod("Append", [typeof(char)])!;
                void AppendIfTruthy(string propName, char ch)
                {
                    var skipLabel = il.DefineLabel();
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldstr, propName);
                    il.Emit(OpCodes.Call, method);
                    il.Emit(OpCodes.Call, runtime.IsTruthy);
                    il.Emit(OpCodes.Brfalse, skipLabel);
                    il.Emit(OpCodes.Ldloc, sbLocal);
                    il.Emit(OpCodes.Ldc_I4, (int)ch);
                    il.Emit(OpCodes.Callvirt, sbAppendChar);
                    il.Emit(OpCodes.Pop);
                    il.MarkLabel(skipLabel);
                }

                // Order per ECMA-262 §22.2.6.4.
                AppendIfTruthy("hasIndices", 'd');
                AppendIfTruthy("global", 'g');
                AppendIfTruthy("ignoreCase", 'i');
                AppendIfTruthy("multiline", 'm');
                AppendIfTruthy("dotAll", 's');
                AppendIfTruthy("unicode", 'u');
                AppendIfTruthy("unicodeSets", 'v');
                AppendIfTruthy("sticky", 'y');

                il.Emit(OpCodes.Ldloc, sbLocal);
                il.Emit(OpCodes.Callvirt, typeof(System.Text.StringBuilder).GetMethod("ToString", Type.EmptyTypes)!);
            });
            // "global" / "ignoreCase" / "multiline" — boolean fields.
            NameMatchBranch("global", () =>
            {
                il.Emit(OpCodes.Ldloc, rxLocal);
                il.Emit(OpCodes.Callvirt, runtime.TSRegExpGlobalGetter);
                il.Emit(OpCodes.Box, _types.Boolean);
            });
            NameMatchBranch("ignoreCase", () =>
            {
                il.Emit(OpCodes.Ldloc, rxLocal);
                il.Emit(OpCodes.Callvirt, runtime.TSRegExpIgnoreCaseGetter);
                il.Emit(OpCodes.Box, _types.Boolean);
            });
            NameMatchBranch("multiline", () =>
            {
                il.Emit(OpCodes.Ldloc, rxLocal);
                il.Emit(OpCodes.Callvirt, runtime.TSRegExpMultilineGetter);
                il.Emit(OpCodes.Box, _types.Boolean);
            });

            // "sticky" / "unicode" / "hasIndices" / "dotAll" / "unicodeSets"
            // — parsed from the flags string. There's no dedicated field for
            // these, so we Contains-check the appropriate char (per ECMA-262
            // §22.2.5.3 flags-string assembly).
            void FlagCharBranch(string propName, char ch)
            {
                NameMatchBranch(propName, () =>
                {
                    il.Emit(OpCodes.Ldloc, rxLocal);
                    il.Emit(OpCodes.Callvirt, runtime.TSRegExpFlagsGetter);
                    // s.Contains(ch) – use Contains(char) overload to dodge
                    // string-literal allocation for the single-char arg.
                    il.Emit(OpCodes.Ldc_I4, (int)ch);
                    il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "Contains", _types.Char));
                    il.Emit(OpCodes.Box, _types.Boolean);
                });
            }
            FlagCharBranch("sticky", 'y');
            FlagCharBranch("unicode", 'u');
            FlagCharBranch("hasIndices", 'd');
            FlagCharBranch("dotAll", 's');
            FlagCharBranch("unicodeSets", 'v');

            // Other property names fall through to GetFieldsProperty so
            // user-set data (`r.foo = 1`) and prototype walks still resolve.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.GetFieldsProperty);
            il.Emit(OpCodes.Ret);
        }

        // Promise (Task<object?> or $Promise) handler - return TSFunction wrappers for then/catch/finally
        il.MarkLabel(promiseLabel);
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
        il.Emit(OpCodes.Callvirt, _types.Type.GetProperty("IsGenericType")!.GetGetMethod()!);
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

        il.MarkLabel(dictLabel);
        // Object.prototype methods short-circuit: return $TSFunction wrappers
        // for the helper. target=null + cached name+length means the wrapper
        // dispatches via InvokeWithThis (the helper's first param is "__this"
        // so _expectsThis=true), letting .call(receiver, ...) inject the right
        // receiver instead of being shadowed by a target-bound prepending
        // that double-applies and trims the wrong tail. Direct dispatch
        // (`obj.method(args)`) still works because compiled-mode method calls
        // route through InvokeMethodValue → InvokeWithThis with the receiver
        // as thisArg. JS-spec name + length surface to user code via fn.name
        // / fn.length introspection.
        void EmitObjProtoMethodCheck(string jsName, MethodBuilder helper, int jsLength)
        {
            var skip = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, skip);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldtoken, helper);
            il.Emit(OpCodes.Ldtoken, helper.DeclaringType!);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle",
                _types.RuntimeMethodHandle, _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Castclass, _types.MethodInfo);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Ldc_I4, jsLength);
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtorWithCache);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(skip);
        }
        EmitObjProtoMethodCheck("hasOwnProperty", runtime.HasOwnPropertyHelperMethod, 1);
        EmitObjProtoMethodCheck("isPrototypeOf",  runtime.IsPrototypeOfHelperMethod, 1);

        // Check for getter accessor via $PropertyDescriptorStore - fully standalone, no reflection
        var getterLocal = il.DeclareLocal(_types.Object);
        var noGetterLabel = il.DefineLabel();

        // Call PDSTryGetGetter(obj, name, out getter)
        il.Emit(OpCodes.Ldarg_0);  // obj
        il.Emit(OpCodes.Ldarg_1);  // name
        il.Emit(OpCodes.Ldloca, getterLocal);  // out getter
        il.Emit(OpCodes.Call, runtime.PDSTryGetGetter);
        il.Emit(OpCodes.Brfalse, noGetterLabel);

        // Getter was found - invoke it via InvokeMethodValue(obj, getter, emptyArgs)
        il.Emit(OpCodes.Ldarg_0);  // receiver (obj)
        il.Emit(OpCodes.Ldloc, getterLocal);  // function (getter)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Newarr, _types.Object);  // empty args array
        il.Emit(OpCodes.Call, runtime.InvokeMethodValue);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(noGetterLabel);

        // dict.TryGetValue(name, out value) ? value : check prototype chain
        var valueLocal = il.DeclareLocal(_types.Object);
        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var protoLocal = il.DeclareLocal(_types.Object);

        // Store the dictionary in a local for later use with BindThis
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Stloc, dictLocal);

        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue"));
        var foundLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, foundLabel);

        // $AbortSignal dict surface (#224, #985): the properties "aborted"/"reason"/
        // "onabort" AND the methods "addEventListener"/"removeEventListener"/
        // "throwIfAborted" on a dynamically-typed (`any`) signal receiver. The typed
        // path intercepts at compile time (AbortSignalEmitter); an `any` receiver lands
        // here. The methods are returned as $TSFunction wrappers (target=null,
        // _expectsThis=true via the "__this" first parameter) so a subsequent
        // InvokeMethodValue injects the signal as the receiver — i.e.
        // `signal.addEventListener('abort', cb)` works from inside a helper, not only at
        // a statically-typed call site. Signals are identified by their "_reasonSet"
        // internal slot — the public keys are computed from the CancellationToken, so
        // they are never own dict entries and always reach this miss path. Name screen
        // runs first to keep ordinary dict misses cheap.
        if (_features.UsesAbortController)
        {
            var notSignalPropLabel = il.DefineLabel();
            var signalNameMatchLabel = il.DefineLabel();
            var strEq = _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String);

            // Returns a $TSFunction wrapping `helper`, bound to no target, so the
            // wrapper's _expectsThis is set (helper's first param is "__this") and
            // InvokeMethodValue passes the signal receiver through. Same shape as
            // EmitObjProtoMethodCheck's hasOwnProperty/isPrototypeOf wrappers.
            void EmitSignalMethodWrapper(MethodBuilder helper, string jsName, int jsLength)
            {
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldtoken, helper);
                il.Emit(OpCodes.Ldtoken, helper.DeclaringType!);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle",
                    _types.RuntimeMethodHandle, _types.RuntimeTypeHandle));
                il.Emit(OpCodes.Castclass, _types.MethodInfo);
                il.Emit(OpCodes.Ldstr, jsName);
                il.Emit(OpCodes.Ldc_I4, jsLength);
                il.Emit(OpCodes.Newobj, runtime.TSFunctionCtorWithCache);
                il.Emit(OpCodes.Ret);
            }

            foreach (var signalProp in new[]
                { "aborted", "reason", "onabort", "addEventListener", "removeEventListener", "throwIfAborted" })
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, signalProp);
                il.Emit(OpCodes.Call, strEq);
                il.Emit(OpCodes.Brtrue, signalNameMatchLabel);
            }
            il.Emit(OpCodes.Br, notSignalPropLabel);

            il.MarkLabel(signalNameMatchLabel);
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldstr, "_reasonSet");
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
            il.Emit(OpCodes.Brfalse, notSignalPropLabel);

            var notSignalAbortedLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "aborted");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, notSignalAbortedLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.AbortSignalGetAborted);
            il.Emit(OpCodes.Box, _types.Boolean);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notSignalAbortedLabel);

            var notSignalReasonLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "reason");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, notSignalReasonLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.AbortSignalGetReason);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notSignalReasonLabel);

            var notSignalOnAbortLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "onabort");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, notSignalOnAbortLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.AbortSignalGetOnAbort);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notSignalOnAbortLabel);

            var notSignalAelLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "addEventListener");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, notSignalAelLabel);
            EmitSignalMethodWrapper(runtime.AbortSignalAddEventListenerThis, "addEventListener", 2);
            il.MarkLabel(notSignalAelLabel);

            var notSignalRelLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "removeEventListener");
            il.Emit(OpCodes.Call, strEq);
            il.Emit(OpCodes.Brfalse, notSignalRelLabel);
            EmitSignalMethodWrapper(runtime.AbortSignalRemoveEventListenerThis, "removeEventListener", 2);
            il.MarkLabel(notSignalRelLabel);

            // Only "throwIfAborted" remains among the screened names.
            EmitSignalMethodWrapper(runtime.AbortSignalThrowIfAbortedThis, "throwIfAborted", 0);

            il.MarkLabel(notSignalPropLabel);
        }

        // Property not found on object - check prototype chain
        // Get prototype: $PropertyDescriptorStore.GetPrototype(obj)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.PDSGetPrototype);
        il.Emit(OpCodes.Stloc, protoLocal);

        // If prototype is null, return undefined
        var returnUndefinedLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, protoLocal);
        il.Emit(OpCodes.Brfalse, returnUndefinedLabel);

        // Recursively call GetProperty(prototype, name) to check prototype chain
        il.Emit(OpCodes.Ldloc, protoLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, method);  // Recursive call to GetProperty
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnUndefinedLabel);
        // ECMA-262: `({}).constructor === Object`. If user hasn't set a custom
        // constructor and no prototype overrides it, return typeof(object) which
        // matches what compiled-mode `Object` resolves to via globalThis.
        var notDictCtorLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, notDictCtorLabel);
        il.Emit(OpCodes.Ldtoken, _types.Object);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notDictCtorLabel);
        // ECMA-262 19.1.3: every plain object inherits from Object.prototype.
        // For Dictionary literals (`{}` etc.) without an explicit prototype,
        // fall back to the ObjectPrototypeField singleton — that's where
        // `valueOf`, `toString`, `propertyIsEnumerable`, and the toLocaleString
        // wrapper live. Required for Test262 patterns that do
        // `({}).toString.call(receiver)` or for ToPrimitive coercion to find
        // the inherited methods on plain dicts. Lazy-populates on first read.
        var protoFallbackMissLabel = il.DefineLabel();
        il.Emit(OpCodes.Call, runtime.ObjectPrototypePopulateMethod);
        var objProtoFallbackLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.ObjectPrototypeField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, objProtoFallbackLocal);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObject.GetMethod("TryGetValue",
            [_types.String, _types.Object.MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, protoFallbackMissLabel);
        il.Emit(OpCodes.Ldloc, objProtoFallbackLocal);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(protoFallbackMissLabel);
        // Return $Undefined.Instance for non-existent properties (JavaScript semantics)
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(foundLabel);

        // If value is a TSFunction, call BindThis(dict) on it
        // to bind 'this' for object method shorthand
        var notTSFunction = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brfalse, notTSFunction);

        // Call func.BindThis(dict)
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Castclass, runtime.TSFunctionType);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Callvirt, runtime.TSFunctionBindThis);

        il.MarkLabel(notTSFunction);
        il.Emit(OpCodes.Ldloc, valueLocal);
        il.Emit(OpCodes.Ret);

        // Map (Dictionary<object, object>) handler - check for "size" property.
        // Gated together with the dispatch arm above.
        if (_features.UsesMap)
        {
            il.MarkLabel(mapLabel);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "size");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            var notMapSizeLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, notMapSizeLabel);
            // Return map.Count as double
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.DictionaryObjectObject);
            il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.DictionaryObjectObject, "Count").GetGetMethod()!);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notMapSizeLabel);
            // For other Map properties, dispatch via GetMapProperty — returns a $BoundMapMethod
            // wrapper for known methods (get/set/has/...) so that `typeof m.get === 'function'`
            // and `m.get.call(m, k)` work on a Map received from another module.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.DictionaryObjectObject);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.GetMapProperty);
            il.Emit(OpCodes.Ret);
        }

        // Set (HashSet<object>) handler - dispatch via GetSetProperty for size +
        // $BoundSetMethod wrappers on known methods (add/has/delete/... plus ES2025
        // set ops). Mirrors the Map handler above.
        if (_features.UsesSet)
        {
            il.MarkLabel(setLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.HashSetOfObject);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.GetSetProperty);
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(listLabel);
        // Check for "length"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notLengthLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notLengthLabel);
        // ECMA-262: `[].constructor === Array`. Compiled `Array` resolves to
        // typeof(IList<object>) — return that here.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notListCtorLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notListCtorLabel);
        il.Emit(OpCodes.Ldtoken, _types.IListOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notListCtorLabel);
        // Numeric-string index — `GetProperty(list, "0")` must return list[0] so
        // that `f[0]` for `f.__proto__ === [1,2,3]` walks the prototype chain
        // and finds the array element. Without this branch the proto-chain walk
        // bottoms out in GetListProperty's null fallback.
        var listIdxLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, listIdxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        var listNotIndexLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, listNotIndexLabel);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, listNotIndexLabel);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, listNotIndexLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldloc, listIdxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(listNotIndexLabel);
        // PDS-stored own descriptor (e.g., RegExp.exec result has `index` /
        // `input` / `groups` attached via PropertyDescriptorStore so the
        // returned value can be a real Array exotic — `instanceof Array` true
        // — while still answering `result.index` / `result.input` correctly).
        // Without this, those metadata properties are invisible.
        var listPdsLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, listPdsLocal);
        il.Emit(OpCodes.Ldloc, listPdsLocal);
        var listSkipPdsLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, listSkipPdsLabel);
        il.Emit(OpCodes.Ldloc, listPdsLocal);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetGetMethod()!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(listSkipPdsLabel);
        // For other properties on List (like methods push, pop, etc.), use GetListProperty
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetListProperty);
        il.Emit(OpCodes.Ret);

        // object[] handler — `arguments`-shape receiver. "length" → array.Length;
        // numeric-string indexes return the element; anything else → undefined.
        il.MarkLabel(objectArrayLabel);
        var objArrNotLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        il.Emit(OpCodes.Brfalse, objArrNotLengthLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ObjectArray);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(objArrNotLengthLabel);
        // Try numeric-string index: int.TryParse(name, out i)
        var objArrIdxLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, objArrIdxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        var objArrNotIndexLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, objArrNotIndexLabel);
        // Bounds check: i >= 0 && i < arr.Length
        il.Emit(OpCodes.Ldloc, objArrIdxLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, objArrNotIndexLabel);
        il.Emit(OpCodes.Ldloc, objArrIdxLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ObjectArray);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Bge, objArrNotIndexLabel);
        // Read element at index
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ObjectArray);
        il.Emit(OpCodes.Ldloc, objArrIdxLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(objArrNotIndexLabel);
        // Other property → undefined
        il.Emit(OpCodes.Ldsfld, runtime.UndefinedInstance);
        il.Emit(OpCodes.Ret);

        // $Array handler - access Elements.Count for "length"
        il.MarkLabel(sharpTSArrayLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notSharpTSArrayLengthLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notSharpTSArrayLengthLabel);
        // Use the LongLength getter — not the int-clamped Length — so `.length`
        // reads up to 2^32 - 1 survive (M3 acceptance: `a.length === 2147483649`).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Callvirt, runtime.TSArrayLongLengthGetter);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notSharpTSArrayLengthLabel);
        // ECMA-262: `[].constructor === Array`. Mirror the listLabel branch.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notTSArrayCtorLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notTSArrayCtorLabel);
        il.Emit(OpCodes.Ldtoken, _types.IListOfObject);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notTSArrayCtorLabel);
        // Numeric-string index — same purpose as the listLabel branch above:
        // proto-chain walks (`f.__proto__ === [1,2,3]; f[0]`) bottom out here
        // when the prototype is a $Array. Without this, GetListProperty returns
        // null for any digit-string name and the array element is invisible.
        var tsArrIdxLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, tsArrIdxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        var tsArrNotIndexLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, tsArrNotIndexLabel);
        il.Emit(OpCodes.Ldloc, tsArrIdxLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, tsArrNotIndexLabel);
        il.Emit(OpCodes.Ldloc, tsArrIdxLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Count").GetGetMethod()!);
        il.Emit(OpCodes.Bge, tsArrNotIndexLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldloc, tsArrIdxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.ListOfObject, "Item").GetGetMethod()!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(tsArrNotIndexLabel);
        // For other properties on $Array (method names like push/pop/sort/etc.),
        // reuse GetListProperty — it returns the $BoundArrayMethod wrapper, and
        // $Array IS a List<object?> by inheritance, so the cast works.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.GetListProperty);
        il.Emit(OpCodes.Ret);

        // $Buffer handler - "length" and "toString". Gated together with the
        // dispatch arm above.
        if (_features.UsesBuffer)
        {
            il.MarkLabel(tsBufferLabel);
            // Check for "length"
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "length");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            var notBufferLenLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, notBufferLenLabel);
            // Get buf.Length
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSBufferType);
            il.Emit(OpCodes.Call, runtime.TSBufferLengthGetter);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notBufferLenLabel);
            // Check for "toString" - return a wrapper that calls ToEncodedString
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "toString");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            var notBufferToStringLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, notBufferToStringLabel);
            // Create a TSFunction wrapper for ToEncodedString
            // For dynamically generated types, we need both method and type tokens
            il.Emit(OpCodes.Ldarg_0);  // target (the buffer)
            il.Emit(OpCodes.Ldtoken, runtime.TSBufferToString);
            il.Emit(OpCodes.Ldtoken, runtime.TSBufferType);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle, _types.RuntimeTypeHandle));
            il.Emit(OpCodes.Castclass, _types.MethodInfo);
            il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notBufferToStringLabel);
            // Unknown buffer property - return null
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }

        // $Stats handler - return method wrappers or property values.
        // Whole block is gated; without UsesFs there's no Stats type to dispatch on.
        if (_features.UsesFs)
        {
        il.MarkLabel(tsStatsLabel);
        // Check for "size" property
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "size");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notStatsSizeLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notStatsSizeLabel);
        // Return stats.size
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.StatsType);
        il.Emit(OpCodes.Call, runtime.StatsSizeGetter);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notStatsSizeLabel);
        // Check for "isFile" method - return TSFunction wrapper
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "isFile");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notStatsIsFileLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notStatsIsFileLabel);
        // Create TSFunction wrapper for isFile
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, runtime.StatsIsFile);
        il.Emit(OpCodes.Ldtoken, runtime.StatsType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle, _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notStatsIsFileLabel);
        // Check for "isDirectory" method
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "isDirectory");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notStatsIsDirLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notStatsIsDirLabel);
        // Create TSFunction wrapper for isDirectory
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, runtime.StatsIsDirectory);
        il.Emit(OpCodes.Ldtoken, runtime.StatsType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle, _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notStatsIsDirLabel);
        // Check for "isSymbolicLink" method
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "isSymbolicLink");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notStatsIsSymlinkLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notStatsIsSymlinkLabel);
        // Create TSFunction wrapper for isSymbolicLink
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, runtime.StatsIsSymbolicLink);
        il.Emit(OpCodes.Ldtoken, runtime.StatsType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle, _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notStatsIsSymlinkLabel);
        // Check for "isBlockDevice" method
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "isBlockDevice");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notStatsIsBlockLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notStatsIsBlockLabel);
        // Create TSFunction wrapper for isBlockDevice
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, runtime.StatsIsBlockDevice);
        il.Emit(OpCodes.Ldtoken, runtime.StatsType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle, _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notStatsIsBlockLabel);
        // Check for "isCharacterDevice" method
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "isCharacterDevice");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notStatsIsCharLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notStatsIsCharLabel);
        // Create TSFunction wrapper for isCharacterDevice
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, runtime.StatsIsCharacterDevice);
        il.Emit(OpCodes.Ldtoken, runtime.StatsType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle, _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notStatsIsCharLabel);
        // Check for "isFIFO" method
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "isFIFO");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notStatsIsFIFOLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notStatsIsFIFOLabel);
        // Create TSFunction wrapper for isFIFO
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, runtime.StatsIsFIFO);
        il.Emit(OpCodes.Ldtoken, runtime.StatsType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle, _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notStatsIsFIFOLabel);
        // Check for "isSocket" method
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "isSocket");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notStatsIsSocketLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notStatsIsSocketLabel);
        // Create TSFunction wrapper for isSocket
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldtoken, runtime.StatsIsSocket);
        il.Emit(OpCodes.Ldtoken, runtime.StatsType);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.MethodBase, "GetMethodFromHandle", _types.RuntimeMethodHandle, _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Castclass, _types.MethodInfo);
        il.Emit(OpCodes.Newobj, runtime.TSFunctionCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notStatsIsSocketLabel);
        // Unknown stats property - return null
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);
        }  // end if (_features.UsesFs) — $Stats handler block

        il.MarkLabel(stringLabel);
        // Check for "length"
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "length");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notStrLenLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notStrLenLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notStrLenLabel);
        // ECMA-262: `"hello".constructor === String`. Compiled mode resolves
        // bare `String` to `typeof(string)` — returning that here makes the
        // strict-equality check hold.
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldstr, "constructor");
        il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
        var notStrCtorLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notStrCtorLabel);
        il.Emit(OpCodes.Ldtoken, _types.String);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notStrCtorLabel);

        // Numeric index: `"hello"[0]` returns "h". Pre-fix returned null
        // because the string fallback didn't honor numeric-string keys —
        // only the typed-string dispatch did.
        var notNumericKeyLabel = il.DefineLabel();
        var strIdxLocal = il.DeclareLocal(_types.Int32);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, strIdxLocal);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Int32, "TryParse", _types.String, _types.Int32.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, notNumericKeyLabel);
        il.Emit(OpCodes.Ldloc, strIdxLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Blt, notNumericKeyLabel);
        il.Emit(OpCodes.Ldloc, strIdxLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bge, notNumericKeyLabel);
        // Return str[idx].ToString()
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldloc, strIdxLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", _types.Int32));
        var charLocalStr = il.DeclareLocal(_types.Char);
        il.Emit(OpCodes.Stloc, charLocalStr);
        il.Emit(OpCodes.Ldloca, charLocalStr);
        il.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.Char, "ToString"));
        il.Emit(OpCodes.Ret);
        il.MarkLabel(notNumericKeyLabel);

        // ECMA-262 7.3.2: walk String.prototype for borrowed-method patterns
        // (`s.valueOf`, `s.toString`, `s.toLowerCase`, etc.). Pre-fix returned
        // null for any property other than length/constructor.
        il.Emit(OpCodes.Call, runtime.StringPrototypePopulateMethod);
        il.Emit(OpCodes.Ldsfld, runtime.StringPrototypeField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Ret);
    }

    private void EmitSetProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Signature forward-declared by DefineRuntimeClassPhase1.
        var method = (MethodBuilder)runtime.SetProperty;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var tsObjectLabel = il.DefineLabel();

        // null check
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        EmitGlobalThisSetRedirect(il, runtime);

        // Proxy check: uses obj.GetType().FullName comparison (no SharpTS.dll dependency)
        var notProxyLabel = il.DefineLabel();
        EmitProxySetPropertyCheck(il, () => il.Emit(OpCodes.Ldarg_0), () => il.Emit(OpCodes.Ldarg_1), () => il.Emit(OpCodes.Ldarg_2), notProxyLabel);

        il.MarkLabel(notProxyLabel);

        // $Object (with setter support) - call obj.SetProperty(name, value)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, tsObjectLabel);

        // $Array — special-case `arr.length = N` (route through $Array.SetLength
        // so truncation/extension uses the sparse-aware path). Other named-
        // property writes on arrays are silently ignored; ECMA-262 §22.1.5
        // permits them but most emitters don't preserve them and the spec
        // compiler tests don't exercise that corner. Must come BEFORE the
        // Dictionary check (since $Array inherits List<object?> which is
        // neither), and BEFORE SetFieldsProperty fallthrough.
        var tsArraySetPropLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, tsArraySetPropLabel);

        // Dictionary
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // $TSFunction — JS functions are objects and support arbitrary property assignment
        // (`fn.x = 42`, `lodash.chunk = function(...){}`). Store as a data descriptor in
        // $PropertyDescriptorStore; GetFunctionMethod's fallback path reads it back. Without
        // this, the assignment would fall through to SetFieldsProperty which is a
        // class-instance path that doesn't match $TSFunction.
        var tsFunctionSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, tsFunctionSetLabel);

        // $CJSModule — `module.exports = X` (or any aliased write) goes through here.
        // Only emit when UsesCjsRequire is on (matching the type emission gate).
        var cjsModuleSetLabel = il.DefineLabel();
        if (_features.UsesCjsRequire)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.CjsModuleType);
            il.Emit(OpCodes.Brtrue, cjsModuleSetLabel);
        }

        // $RegExp — `r.lastIndex = value` must coerce `value` via JS
        // ToLength (string "1.9" → 1, NaN → 0, etc.) and write the internal
        // int32 slot. Without the special case the assignment falls through
        // to SetFieldsProperty which stores the boxed value on a generic
        // PDS bag — subsequent built-in matchers ignore it and use the
        // stale internal slot. Test262 builtin-coerce-lastindex.js etc.
        // require coerce+round-trip.
        var tsRegExpSetLabel = il.DefineLabel();
        if (_features.UsesRegExp)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSRegExpType);
            il.Emit(OpCodes.Brtrue, tsRegExpSetLabel);
        }

        // System.Type (class reference used as value, e.g. `Scalar.PLAIN = 'x'`). JS allows
        // arbitrary static property assignment on classes; we store them in PropertyDescriptorStore.
        var typeSetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.Type);
        il.Emit(OpCodes.Brtrue, typeSetLabel);

        // List<object?> — raw arrays from `[...]` literal / Array.prototype.concat
        // / etc. accept arbitrary named property assignment per ECMA-262 §23.1.5
        // [[DefineOwnProperty]]. Numeric indices are handled via SetIndex; only
        // string keys land here. Store named-non-numeric writes in PDS as data
        // descriptors so GetProperty / hasOwn / gOPD round-trip. Pre-fix these
        // fell to SetFieldsProperty which silently dropped them.
        var listSetPropLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, listSetPropLabel);

        // Not a dict or $Object or $TSFunction or $CJSModule or Type - try SetFieldsProperty for class instances
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetFieldsProperty);
        il.Emit(OpCodes.Ret);

        // List<object?> handler: same shape as $TSArray's non-length path.
        il.MarkLabel(listSetPropLabel);
        {
            // Skip if key is "length" or a numeric index — those write paths
            // belong to SetIndex (numeric) / a dedicated length path. Silent
            // no-op for "length" matches current behavior; named numeric
            // string falls through to PDS (close enough, integer-key writes
            // through SetProperty are rare).
            var listSetIsLengthLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "length");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brtrue, listSetIsLengthLabel);
            // Frozen guard.
            var listSetNotFrozenLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.PDSIsFrozen);
            il.Emit(OpCodes.Brfalse, listSetNotFrozenLabel);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(listSetNotFrozenLabel);
            // Existing-descriptor writable=false guard.
            var listSetExistingDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, listSetExistingDescLocal);
            var listSetWritableLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, listSetExistingDescLocal);
            il.Emit(OpCodes.Brfalse, listSetWritableLabel);
            il.Emit(OpCodes.Ldloc, listSetExistingDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, listSetWritableLabel);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(listSetWritableLabel);
            // Install fresh data descriptor with the value.
            EmitDefineDataDescriptorFromValue(il, runtime);
            il.MarkLabel(listSetIsLengthLabel);
            il.Emit(OpCodes.Ret);
        }

        // $RegExp handler: route `r.lastIndex = value` to the typed setter
        // with JS ToLength coercion. Other property writes fall through to
        // SetFieldsProperty so user data-property assignments
        // (`Object.defineProperty(r, 'foo', {writable:true}); r.foo = ...`)
        // still hit the user-property bag.
        if (_features.UsesRegExp)
        {
            il.MarkLabel(tsRegExpSetLabel);

            // PDS-first: if user installed an accessor descriptor with a
            // setter, invoke it. If a data descriptor with writable=false
            // is present, silently swallow (non-strict; strict-mode is
            // handled by the strict variant). Mirrors the GET-side fix
            // for spec-aligned override semantics on $RegExp instances.
            var setNoPdsLabel = il.DefineLabel();
            var setPdsDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, setPdsDescLocal);
            il.Emit(OpCodes.Ldloc, setPdsDescLocal);
            il.Emit(OpCodes.Brfalse, setNoPdsLabel);

            // Accessor setter? Setter slot non-null → InvokeWithThis(rx, value).
            var setNoAccessorLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, setPdsDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
            var setterValueLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Stloc, setterValueLocal);
            il.Emit(OpCodes.Ldloc, setterValueLocal);
            il.Emit(OpCodes.Brfalse, setNoAccessorLabel);
            var setterFnLocal = il.DeclareLocal(runtime.TSFunctionType);
            il.Emit(OpCodes.Ldloc, setterValueLocal);
            il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
            il.Emit(OpCodes.Stloc, setterFnLocal);
            il.Emit(OpCodes.Ldloc, setterFnLocal);
            il.Emit(OpCodes.Brfalse, setNoAccessorLabel);
            il.Emit(OpCodes.Ldloc, setterFnLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, _types.Object);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Callvirt, runtime.TSFunctionInvokeWithThis);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(setNoAccessorLabel);

            // No setter slot — check getter. If getter present (accessor
            // descriptor), this is getter-only → silently no-op (non-strict).
            var setSilentlyIgnoreLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, setPdsDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorGetter.GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, setSilentlyIgnoreLabel);
            // No getter and no setter → data descriptor. Honor writable bit.
            il.Emit(OpCodes.Ldloc, setPdsDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, setSilentlyIgnoreLabel);
            il.Emit(OpCodes.Ldloc, setPdsDescLocal);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorValue.GetSetMethod()!);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(setSilentlyIgnoreLabel);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(setNoPdsLabel);

            var notLastIndexLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "lastIndex");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, notLastIndexLabel);

            // ECMA-262: lastIndex is an ordinary writable data property — store
            // the assigned value as-is (ToLength is deferred to RegExpBuiltinExec).
            // Only an OBJECT (whose ToLength would run user `valueOf`) needs the
            // boxed slot to defer that call + preserve identity; primitives
            // (null/undefined/number/string/bool) ToLength with no user code, so
            // they fold straight into the typed int slot (and clear the box).
            // NB: JS `null` ToLengths to 0 — it must take the primitive path, not
            // the box (a boxed C# null is indistinguishable from "no box").
            var numericSetLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Brfalse, numericSetLabel);                 // null
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Isinst, _types.Double);
            il.Emit(OpCodes.Brtrue, numericSetLabel);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Isinst, _types.String);
            il.Emit(OpCodes.Brtrue, numericSetLabel);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Isinst, _types.Boolean);
            il.Emit(OpCodes.Brtrue, numericSetLabel);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Isinst, runtime.UndefinedType);
            il.Emit(OpCodes.Brtrue, numericSetLabel);
            // object → rx._lastIndexBoxed = value (defer ToLength/valueOf to exec)
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSRegExpType);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, _tsRegExpLastIndexBoxedField);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(numericSetLabel);
            // primitive: rx._lastIndex = ToLength(value); rx._lastIndexBoxed = null
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSRegExpType);
            il.Emit(OpCodes.Ldarg_2);
            EmitToLengthBoxed(il, runtime);
            il.Emit(OpCodes.Callvirt, runtime.TSRegExpLastIndexSetter);  // also clears boxed
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notLastIndexLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.SetFieldsProperty);
            il.Emit(OpCodes.Ret);
        }

        // System.Type handler: store as data descriptor in PropertyDescriptorStore.
        // Read path (EmitGetProperty) looks it up before falling through to .NET member
        // resolution, so writes become visible as reads.
        il.MarkLabel(typeSetLabel);
        {
            // Per ECMA-262 §17, constructor static "prototype"/"name"/"length"
            // are non-writable; non-strict writes silently no-op. Same for
            // Number constants (MAX_VALUE etc.) which are W:F,E:F,C:F.
            var typeSetSkipLabel = il.DefineLabel();
            void EmitTypeSetSkipName(string n)
            {
                var notNameLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, n);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, notNameLabel);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(notNameLabel);
            }
            EmitTypeSetSkipName("prototype");
            EmitTypeSetSkipName("name");
            EmitTypeSetSkipName("length");
            // Number constants — non-writable on the Number constructor.
            var notNumberTypeForSetLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, _types.Double);
            il.Emit(OpCodes.Call, _types.Type.GetMethod("GetTypeFromHandle")!);
            il.Emit(OpCodes.Bne_Un, notNumberTypeForSetLabel);
            EmitTypeSetSkipName("MAX_VALUE");
            EmitTypeSetSkipName("MIN_VALUE");
            EmitTypeSetSkipName("NaN");
            EmitTypeSetSkipName("POSITIVE_INFINITY");
            EmitTypeSetSkipName("NEGATIVE_INFINITY");
            EmitTypeSetSkipName("MAX_SAFE_INTEGER");
            EmitTypeSetSkipName("MIN_SAFE_INTEGER");
            EmitTypeSetSkipName("EPSILON");
            il.MarkLabel(notNumberTypeForSetLabel);

            EmitDefineDataDescriptorFromValue(il, runtime);
            il.Emit(OpCodes.Ret);
        }

        // $CJSModule handler — only "exports" is writable; others are no-ops (spec behavior).
        if (_features.UsesCjsRequire)
        {
            il.MarkLabel(cjsModuleSetLabel);
            EmitCjsModuleExportsSetBranch(il, runtime);
        }

        // $Array handler — `arr.length = N` routes through SetLength. Any
        // other name falls off into the normal silent-ignore (JS permits
        // arbitrary named writes on arrays but we don't persist them).
        // ECMA-262 23.1.4.1 [[DefineOwnProperty]] for "length": if
        //   ToNumber(value) !== ToUint32(value)
        // (i.e. value is not a non-negative integer ≤ 2^32 - 1), throw
        // RangeError. Pre-fix Convert.ToInt64 rounded `1.5 → 2` silently.
        il.MarkLabel(tsArraySetPropLabel);
        {
            var notLengthLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "length");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, notLengthLabel);

            // Coerce value through ToNumber (handles strings, booleans, etc.)
            // then enforce ToUint32 round-trip. The .NET `Convert.ToInt64` path
            // truncates fractional parts; we need to flag fractional / NaN /
            // out-of-range as a RangeError instead.
            var doubleValLocal = il.DeclareLocal(_types.Double);
            var u32Local = il.DeclareLocal(_types.Int64);
            var validLengthLabel = il.DefineLabel();
            var rangeErrorLabel = il.DefineLabel();

            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.ToNumber);
            il.Emit(OpCodes.Stloc, doubleValLocal);

            // Reject NaN / +Infinity / -Infinity via IsFinite.
            il.Emit(OpCodes.Ldloc, doubleValLocal);
            il.Emit(OpCodes.Call, _types.Double.GetMethod("IsFinite", [_types.Double])!);
            il.Emit(OpCodes.Brfalse, rangeErrorLabel);

            // ToUint32 reciprocity: cast to long, ensure 0..2^32-1 inclusive,
            // and that the round-trip back to double matches the original.
            il.Emit(OpCodes.Ldloc, doubleValLocal);
            il.Emit(OpCodes.Conv_I8);
            il.Emit(OpCodes.Stloc, u32Local);
            // negative → throw
            il.Emit(OpCodes.Ldloc, u32Local);
            il.Emit(OpCodes.Ldc_I8, 0L);
            il.Emit(OpCodes.Blt, rangeErrorLabel);
            // > uint.MaxValue → throw
            il.Emit(OpCodes.Ldloc, u32Local);
            il.Emit(OpCodes.Ldc_I8, (long)uint.MaxValue);
            il.Emit(OpCodes.Bgt, rangeErrorLabel);
            // round-trip mismatch (fractional component) → throw
            il.Emit(OpCodes.Ldloc, u32Local);
            il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Ldloc, doubleValLocal);
            il.Emit(OpCodes.Bne_Un, rangeErrorLabel);
            il.Emit(OpCodes.Br, validLengthLabel);

            il.MarkLabel(rangeErrorLabel);
            il.Emit(OpCodes.Ldstr, "Invalid array length");
            il.Emit(OpCodes.Newobj, runtime.TSRangeErrorCtor);
            il.Emit(OpCodes.Call, runtime.CreateException);
            il.Emit(OpCodes.Throw);

            il.MarkLabel(validLengthLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSArrayType);
            il.Emit(OpCodes.Ldloc, u32Local);
            il.Emit(OpCodes.Callvirt, runtime.TSArraySetLength);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(notLengthLabel);
            // Other named writes on arrays go to PDS as data descriptors.
            // ECMA-262 23.1.5: arrays are exotic objects but accept arbitrary
            // named property assignment via [[DefineOwnProperty]]. Test262
            // patterns like `var arr = []; arr.foo = ...; arr.foo()` rely on
            // this. GetFieldsProperty's PDS-data-descriptor arm reads it back.
            // Pre-fix unconditionally overwrote — Object.freeze(arr) didn't
            // block `arr.foo = "x"` because the existing PDS descriptor's
            // writable bit (false post-freeze via AND-mask) was ignored.
            {
                // Honor frozen state: if Object.isFrozen(arr), silently no-op
                // (non-strict). Strict callers use SetPropertyStrict which
                // can throw.
                var arrFrozenLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, runtime.PDSIsFrozen);
                il.Emit(OpCodes.Brfalse, arrFrozenLabel);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(arrFrozenLabel);

                // Honor existing-descriptor writable=false: if there's a PDS
                // data descriptor for this key with writable=false, silently
                // no-op. Accessor descriptors fall through (defining a value
                // over an accessor is handled by PDSDefineProperty).
                var arrNotWritableLabel = il.DefineLabel();
                var arrExistingDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
                il.Emit(OpCodes.Stloc, arrExistingDescLocal);
                il.Emit(OpCodes.Ldloc, arrExistingDescLocal);
                il.Emit(OpCodes.Brfalse, arrNotWritableLabel);
                // Has descriptor; check writable.
                il.Emit(OpCodes.Ldloc, arrExistingDescLocal);
                il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
                il.Emit(OpCodes.Brtrue, arrNotWritableLabel);
                // Not writable — silent no-op.
                il.Emit(OpCodes.Ret);
                il.MarkLabel(arrNotWritableLabel);

                EmitDefineDataDescriptorFromValue(il, runtime);
            }
            il.Emit(OpCodes.Ret);
        }

        // $TSFunction handler: ECMA-262 §10.1.9 [[Set]] honors non-extensibility for new
        // properties. Gate via PDSCanAddProperty so `Object.preventExtensions(fn); fn.x = v`
        // silently no-ops (non-strict). Existing PDS entries still update.
        il.MarkLabel(tsFunctionSetLabel);
        {
            var tsFnDoSetLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSCanAddProperty);
            il.Emit(OpCodes.Brtrue, tsFnDoSetLabel);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(tsFnDoSetLabel);

            EmitDefineDataDescriptorFromValue(il, runtime);
            il.Emit(OpCodes.Ret);
        }

        // $Object handler. First check PDS for a setter accessor descriptor
        // (defineProperty-installed). If present, invoke it (passing $TSObject
        // as `this`) — TSObject.SetProperty doesn't know about PDS-stored
        // setters. Otherwise delegate to TSObject.SetProperty for the dict /
        // _getters / _setters fast path.
        il.MarkLabel(tsObjectLabel);
        {
            var tsObjPdsSetterLocal = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, tsObjPdsSetterLocal);
            il.Emit(OpCodes.Call, runtime.PDSTryGetSetter);
            var tsObjNoPdsSetterLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, tsObjNoPdsSetterLabel);
            // Invoke PDS setter.
            EmitInvokePdsSetterWithValueAndReturn(il, runtime, tsObjPdsSetterLocal);
            il.MarkLabel(tsObjNoPdsSetterLabel);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, runtime.TSObjectSetProperty);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);

        // $AbortSignal dict surface (#985): `signal.onabort = cb` on a dynamically-
        // typed (`any`) signal receiver. The typed path intercepts at compile time
        // (AbortSignalEmitter.TryEmitPropertySet); an `any` receiver lands here and
        // would otherwise store a plain "onabort" dict key that FireAbortEvent (which
        // reads the internal "_onabort" slot) never sees — so the handler never fired.
        // Signals are identified by their "_reasonSet" internal slot. Gated on
        // UsesAbortController so non-signal programs pay nothing; mirrors the GetProperty
        // signal branch (#224).
        if (_features.UsesAbortController)
        {
            var notSignalOnAbortSet = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, "onabort");
            il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
            il.Emit(OpCodes.Brfalse, notSignalOnAbortSet);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
            il.Emit(OpCodes.Ldstr, "_reasonSet");
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
            il.Emit(OpCodes.Brfalse, notSignalOnAbortSet);
            il.Emit(OpCodes.Ldarg_0);  // signal
            il.Emit(OpCodes.Ldarg_2);  // handler
            il.Emit(OpCodes.Call, runtime.AbortSignalSetOnAbort);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notSignalOnAbortSet);
        }

        // For dictionaries, check frozen/sealed tables and silently ignore if frozen/sealed
        var sealedCheckLabel = il.DefineLabel();
        var doSetLabel = il.DefineLabel();
        var valueLocal = il.DeclareLocal(_types.Object);

        // Check if frozen: _frozenObjects.TryGetValue(obj, out _).
        // Per ECMA-262, freeze only forbids writes to DATA properties — accessor
        // setters still fire because the descriptor's writable bit doesn't apply
        // to accessors. So when there's a PDS setter for this key, fall through
        // to the doSetLabel path (which invokes it). Pre-fix dropped frozen
        // accessor writes silently — broke test262 15.2.3.9-2-c-{2,3,4}.
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var frozenNotFoundLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, frozenNotFoundLabel);
        // Frozen — only allow if there's a PDS accessor setter for this key.
        var frozenAccessorSetterLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, frozenAccessorSetterLocal);
        il.Emit(OpCodes.Call, runtime.PDSTryGetSetter);
        il.Emit(OpCodes.Brfalse, nullLabel); // No setter, frozen data — silent return
        il.Emit(OpCodes.Br, doSetLabel);     // Has setter — proceed (doSetLabel re-fetches via PDSTryGetSetter)
        il.MarkLabel(frozenNotFoundLabel);

        // Check if sealed and property doesn't exist
        il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var extensibleCheckLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, extensibleCheckLabel); // Not sealed, check extensibility

        // Object is sealed - check if property exists (dict OR PDS). Pre-fix
        // only checked the backing dict, so an accessor-only own property
        // (defineProperty with get/set + configurable:false) was treated as
        // missing and the write silently dropped — including its setter.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Brtrue, doSetLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Brfalse, nullLabel); // Property doesn't exist, silently return
        il.Emit(OpCodes.Br, doSetLabel); // Property exists on sealed object, proceed to set

        // Check extensibility via $PropertyDescriptorStore.CanAddProperty - fully standalone, no reflection
        il.MarkLabel(extensibleCheckLabel);
        il.Emit(OpCodes.Ldarg_0);  // obj
        il.Emit(OpCodes.Ldarg_1);  // name
        il.Emit(OpCodes.Call, runtime.PDSCanAddProperty);
        il.Emit(OpCodes.Brfalse, nullLabel);  // Cannot add property, silently return

        // Actually set the property
        il.MarkLabel(doSetLabel);

        // Check for setter accessor via $PropertyDescriptorStore - fully standalone, no reflection
        var setterLocal = il.DeclareLocal(_types.Object);
        var noSetterLabel = il.DefineLabel();

        // Call PDSTryGetSetter(obj, name, out setter)
        il.Emit(OpCodes.Ldarg_0);  // obj
        il.Emit(OpCodes.Ldarg_1);  // name
        il.Emit(OpCodes.Ldloca, setterLocal);  // out setter
        il.Emit(OpCodes.Call, runtime.PDSTryGetSetter);
        il.Emit(OpCodes.Brfalse, noSetterLabel);

        // Setter was found - invoke it via InvokeMethodValue(obj, setter, [value])
        EmitInvokePdsSetterWithValueAndReturn(il, runtime, setterLocal);

        il.MarkLabel(noSetterLabel);

        // Check if property is writable via $PropertyDescriptorStore - fully standalone, no reflection
        il.Emit(OpCodes.Ldarg_0);  // obj
        il.Emit(OpCodes.Ldarg_1);  // name
        il.Emit(OpCodes.Call, runtime.PDSIsWritable);
        il.Emit(OpCodes.Brfalse, nullLabel);  // Not writable, silently return

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits inline IL that coerces the boxed-object value at the top of the
    /// stack to int32 via the JS ToLength algorithm: null/undefined/NaN → 0,
    /// false → 0, true → 1, double via truncate (clamped to int32), int via
    /// pass-through, string via TryParse → double-path (or 0 on parse
    /// failure), other types → 0. Used by SetProperty's $RegExp arm so
    /// `r.lastIndex = '1.9'` stores 1.
    /// </summary>
    private void EmitToLengthBoxed(ILGenerator il, EmittedRuntime runtime)
    {
        var localVal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Stloc, localVal);

        var nullLabel = il.DefineLabel();
        var undefinedLabel = il.DefineLabel();
        var doubleLabel = il.DefineLabel();
        var intLabel = il.DefineLabel();
        var boolLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var doneLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, localVal);
        il.Emit(OpCodes.Brfalse, nullLabel);

        il.Emit(OpCodes.Ldloc, localVal);
        il.Emit(OpCodes.Isinst, runtime.UndefinedType);
        il.Emit(OpCodes.Brtrue, undefinedLabel);

        il.Emit(OpCodes.Ldloc, localVal);
        il.Emit(OpCodes.Isinst, _types.Double);
        il.Emit(OpCodes.Brtrue, doubleLabel);

        il.Emit(OpCodes.Ldloc, localVal);
        il.Emit(OpCodes.Isinst, _types.Int32);
        il.Emit(OpCodes.Brtrue, intLabel);

        il.Emit(OpCodes.Ldloc, localVal);
        il.Emit(OpCodes.Isinst, _types.Boolean);
        il.Emit(OpCodes.Brtrue, boolLabel);

        il.Emit(OpCodes.Ldloc, localVal);
        il.Emit(OpCodes.Isinst, _types.String);
        il.Emit(OpCodes.Brtrue, stringLabel);

        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(undefinedLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(doubleLabel);
        var dTmp = il.DeclareLocal(_types.Double);
        il.Emit(OpCodes.Ldloc, localVal);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Stloc, dTmp);

        // NaN check: (d == d) is false iff d is NaN.
        var dNonNanLabel = il.DefineLabel();
        var dPositiveLabel = il.DefineLabel();
        var dClampLabel = il.DefineLabel();
        var dInRangeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, dTmp);
        il.Emit(OpCodes.Ldloc, dTmp);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, dNonNanLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(dNonNanLabel);
        il.Emit(OpCodes.Ldloc, dTmp);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Bgt, dPositiveLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(dPositiveLabel);
        il.Emit(OpCodes.Ldloc, dTmp);
        il.Emit(OpCodes.Ldc_R8, (double)int.MaxValue);
        il.Emit(OpCodes.Blt, dInRangeLabel);
        il.Emit(OpCodes.Ldc_I4, int.MaxValue);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(dInRangeLabel);
        il.Emit(OpCodes.Ldloc, dTmp);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(intLabel);
        il.Emit(OpCodes.Ldloc, localVal);
        il.Emit(OpCodes.Unbox_Any, _types.Int32);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(boolLabel);
        il.Emit(OpCodes.Ldloc, localVal);
        il.Emit(OpCodes.Unbox_Any, _types.Boolean);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(stringLabel);
        var sTmp = il.DeclareLocal(_types.Double);
        var parseFailLabel = il.DefineLabel();
        var sPosLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, localVal);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Ldc_I4, (int)System.Globalization.NumberStyles.Float);
        il.Emit(OpCodes.Call, _types.GetProperty(typeof(System.Globalization.CultureInfo), "InvariantCulture").GetGetMethod()!);
        il.Emit(OpCodes.Ldloca, sTmp);
        il.Emit(OpCodes.Call, typeof(double).GetMethod("TryParse",
            [_types.String, typeof(System.Globalization.NumberStyles), typeof(IFormatProvider), typeof(double).MakeByRefType()])!);
        il.Emit(OpCodes.Brfalse, parseFailLabel);
        il.Emit(OpCodes.Ldloc, sTmp);
        il.Emit(OpCodes.Ldloc, sTmp);
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, parseFailLabel);
        il.Emit(OpCodes.Ldloc, sTmp);
        il.Emit(OpCodes.Ldc_R8, 0.0);
        il.Emit(OpCodes.Bgt, sPosLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(sPosLabel);
        il.Emit(OpCodes.Ldloc, sTmp);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Br, doneLabel);

        il.MarkLabel(parseFailLabel);
        il.Emit(OpCodes.Ldc_I4_0);

        il.MarkLabel(doneLabel);
    }

    /// <summary>
    /// Emits SetPropertyStrict(object obj, string name, object value, bool strictMode) -> void
    /// In strict mode, throws TypeError for modifications to frozen objects or new properties on sealed objects.
    /// </summary>
    private void EmitSetPropertyStrict(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetPropertyStrict",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.String, _types.Object, _types.Boolean]
        );
        runtime.SetPropertyStrict = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();

        // null check
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Without this redirect, a strict-mode top-level `this.foo = v`
        // (compiled `this` resolves to the globalThis sentinel) fell through
        // to SetFieldsPropertyStrict and was dropped — e.g. Test262
        // Object/create 15.2.3.5-4-177 / defineProperty 15.2.3.6-3-230 set
        // `this.value` / `this.get` and reuse `this` as a descriptor.
        EmitGlobalThisSetRedirect(il, runtime);

        // Check if $Object
        var sharpTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, sharpTSObjectLabel);

        // Dictionary
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // $TSFunction — mirror the non-strict SetProperty branch (functions as objects carry
        // user-assigned properties through PDSDefineProperty).
        var tsFunctionSetStrictLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, tsFunctionSetStrictLabel);

        // $CJSModule — mirror the non-strict branch. Gated on UsesCjsRequire.
        var cjsModuleSetStrictLabel = il.DefineLabel();
        if (_features.UsesCjsRequire)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.CjsModuleType);
            il.Emit(OpCodes.Brtrue, cjsModuleSetStrictLabel);
        }

        // $Array / List<object?> — named property write on an array in strict
        // mode. The non-strict SetProperty has dedicated array branches (length
        // coercion, numeric, PDS data descriptors); without a strict equivalent
        // these writes fell to SetFieldsPropertyStrict, which silently dropped
        // them, so `arr.foo = fn; arr.foo()` saw `foo === undefined` under "use
        // strict" (Test262 Array slice/splice S15.4.4.*_A*_T*, the common
        // `arr.getClass = Object.prototype.toString` pattern). Detect arrays here
        // and route to arraySetStrictLabel, which enforces strict frozen /
        // non-writable throws then reuses the non-strict store logic.
        var arraySetStrictLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, arraySetStrictLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObject);
        il.Emit(OpCodes.Brtrue, arraySetStrictLabel);

        // Not a dict or $Object or $TSFunction or $CJSModule or array - fall back to SetFieldsPropertyStrict.
        // NOTE (#1131): unlike the non-strict SetProperty, this variant has no
        // dedicated Proxy / $RegExp / System.Type receiver branches — those
        // receivers fall through here. Preserved as-is by the strict/non-strict
        // dedup (behavior-preserving); tracked as drift in the epic notes.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Call, runtime.SetFieldsPropertyStrict);
        il.Emit(OpCodes.Ret);

        // $Array / List<object?> strict handler: ECMA-262 §10.4.2.1 / OrdinarySet
        // step 5 — a frozen array or an own non-writable data property makes the
        // assignment fail; in strict mode PutValue then throws a TypeError. We
        // honor those two cases here, then delegate the actual store to the
        // non-strict SetProperty (shared length/numeric/PDS-data-descriptor path).
        il.MarkLabel(arraySetStrictLabel);
        {
            var arrayDoStoreLabel = il.DefineLabel();

            // Frozen array → throw "Cannot assign to read only property 'name'".
            var arrayNotFrozenLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.PDSIsFrozen);
            il.Emit(OpCodes.Brfalse, arrayNotFrozenLabel);
            EmitThrowTypeErrorWithName(il, runtime, "Cannot assign to read only property '", "' of object '[object Array]'");
            il.MarkLabel(arrayNotFrozenLabel);

            // Own non-writable DATA descriptor → throw. Accessor descriptors
            // (setter present) and writable/absent descriptors fall through to
            // the store, where SetProperty invokes the setter or overwrites.
            var arrayDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, arrayDescLocal);
            il.Emit(OpCodes.Ldloc, arrayDescLocal);
            il.Emit(OpCodes.Brfalse, arrayDoStoreLabel); // no descriptor → store
            il.Emit(OpCodes.Ldloc, arrayDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorSetter.GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, arrayDoStoreLabel); // accessor → delegate (invokes setter)
            il.Emit(OpCodes.Ldloc, arrayDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorWritable.GetGetMethod()!);
            il.Emit(OpCodes.Brtrue, arrayDoStoreLabel); // writable → store
            EmitThrowTypeErrorWithName(il, runtime, "Cannot assign to read only property '", "' of object '[object Array]'");

            il.MarkLabel(arrayDoStoreLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, runtime.SetProperty);
            il.Emit(OpCodes.Ret);
        }

        // $CJSModule strict handler — same as non-strict for now. Gated.
        if (_features.UsesCjsRequire)
        {
            il.MarkLabel(cjsModuleSetStrictLabel);
            EmitCjsModuleExportsSetBranch(il, runtime);
        }

        // $TSFunction handler: create data descriptor with the value, store via PDSDefineProperty
        il.MarkLabel(tsFunctionSetStrictLabel);
        EmitDefineDataDescriptorFromValue(il, runtime);
        il.Emit(OpCodes.Ret);

        // $Object - call SetPropertyStrict
        il.MarkLabel(sharpTSObjectLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Callvirt, runtime.TSObjectSetPropertyStrict);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // For dictionaries, check frozen/sealed tables
        var frozenCheckLabel = il.DefineLabel();
        var sealedCheckLabel = il.DefineLabel();
        var doSetLabel = il.DefineLabel();
        var valueLocal = il.DeclareLocal(_types.Object);

        // Check if frozen: _frozenObjects.TryGetValue(obj, out _)
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        il.Emit(OpCodes.Brfalse, sealedCheckLabel); // Not frozen, check sealed

        // Object is frozen - check strict mode
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Brfalse, nullLabel); // Not strict, silently return

        // Strict mode and frozen - throw TypeError
        EmitThrowTypeErrorWithName(il, runtime, "Cannot assign to read only property '", "' of object");

        // Check if sealed and property doesn't exist
        il.MarkLabel(sealedCheckLabel);
        il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var extensibleCheckLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, extensibleCheckLabel); // Not sealed, check extensibility

        // Object is sealed - check if property exists
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "ContainsKey", _types.String));
        il.Emit(OpCodes.Brtrue, doSetLabel); // Property exists, can modify

        // Property doesn't exist and object is sealed - check strict mode
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Brfalse, nullLabel); // Not strict, silently return

        // Strict mode and sealed with new property - throw TypeError
        EmitThrowTypeErrorWithName(il, runtime, "Cannot add property '", "' to a sealed object");

        // Check extensibility via $PropertyDescriptorStore.CanAddProperty - fully standalone, no reflection
        il.MarkLabel(extensibleCheckLabel);
        il.Emit(OpCodes.Ldarg_0);  // obj
        il.Emit(OpCodes.Ldarg_1);  // name
        il.Emit(OpCodes.Call, runtime.PDSCanAddProperty);
        il.Emit(OpCodes.Brtrue, doSetLabel);  // Can add property, proceed to set

        // Cannot add property (non-extensible) - check strict mode
        il.Emit(OpCodes.Ldarg_3);  // strictMode
        il.Emit(OpCodes.Brfalse, nullLabel);  // Not strict, silently return

        // Strict mode and non-extensible with new property - throw TypeError
        EmitThrowTypeErrorWithName(il, runtime, "Cannot add property '", "' to a non-extensible object");

        // Actually set the property. Mirrors the non-strict SetProperty doSet
        // arm: honor a PDS accessor setter, and an existing non-writable data
        // descriptor (or getter-only accessor). Pre-fix the strict path skipped
        // straight to dict.set_Item, so under "use strict": (1) an accessor
        // setter was bypassed and overwritten with a data value, and (2) writes
        // to a writable:false property neither threw nor were suppressed —
        // `verifyProperty`'s isWritable probe then saw the write succeed (Test262
        // Object/defineProperty 15.2.3.6-3-181 et al.).
        il.MarkLabel(doSetLabel);

        // PDS accessor setter present → invoke it (ECMA-262 OrdinarySet: a setter
        // fires regardless of strictness).
        var strictSetterLocal = il.DeclareLocal(_types.Object);
        var strictNoSetterLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloca, strictSetterLocal);
        il.Emit(OpCodes.Call, runtime.PDSTryGetSetter);
        il.Emit(OpCodes.Brfalse, strictNoSetterLabel);
        EmitInvokePdsSetterWithValueAndReturn(il, runtime, strictSetterLocal);
        il.MarkLabel(strictNoSetterLabel);

        // Non-writable (data writable:false, or getter-only accessor) → strict
        // throws TypeError (ECMA-262 §6.2.5.6 / PutValue), sloppy silently
        // returns. PDSIsWritable returns true when no descriptor exists.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSIsWritable);
        var strictWritableLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, strictWritableLabel);
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Brfalse, nullLabel); // sloppy → silent return
        EmitThrowTypeErrorWithName(il, runtime, "Cannot assign to read only property '", "' of object");
        il.MarkLabel(strictWritableLabel);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item"));
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits SetIndexStrict(object obj, object index, object value, bool strictMode) -> void
    /// In strict mode, throws TypeError for modifications to frozen/sealed arrays.
    /// </summary>
    private void EmitSetIndexStrict(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetIndexStrict",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Object, _types.Object, _types.Boolean]
        );
        runtime.SetIndexStrict = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var sharpTSArrayLabel = il.DefineLabel();
        var listLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();

        // null check
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullLabel);

        // Symbol-keyed write (symbols work on any receiver type) — route to the
        // non-strict SetIndex, which has the symbol-key handler (registered
        // accessor setter, else symbol-dict store with extensibility checks).
        // SetIndexStrict otherwise dispatches only by RECEIVER type ($Array /
        // List / Dictionary) and would ToString a symbol key into a bogus string
        // key, dropping the write — so under "use strict" `obj[sym] = v` and
        // `obj[Symbol.iterator] = fn` were lost (Test262 Object/
        // getOwnPropertySymbols object-contains-symbol-property-without-description,
        // Array/from iter-get-iter-val-err).
        // The store itself (registered setter / symbol-dict write) is delegated
        // to the non-strict SetIndex, but its symbol handler silently no-ops on a
        // frozen/sealed/non-extensible receiver. In strict mode those must throw
        // a TypeError (ECMA-262 PutValue), so enforce that here first:
        //   frozen           → always throw (props non-writable + non-extensible);
        //   sealed/non-ext    → throw only when the symbol key is NEW (adding it);
        //   otherwise         → delegate to SetIndex (store / update / invoke setter).
        // (Test262 Object/freeze frozen-object-contains-symbol-properties-strict.)
        var notSymbolKeyLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.IsSymbolMethod);
        il.Emit(OpCodes.Brfalse, notSymbolKeyLabel);

        var symThrowLabel = il.DefineLabel();
        var symRouteLabel = il.DefineLabel();
        var symStateTmp = il.DeclareLocal(_types.Object);
        var cwtTryGetValue = _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType());

        // frozen → throw.
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, symStateTmp);
        il.Emit(OpCodes.Callvirt, cwtTryGetValue);
        il.Emit(OpCodes.Brtrue, symThrowLabel);

        // sealed OR non-extensible → throw only if the symbol key is not already present.
        var symSealedOrNonExtLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, symStateTmp);
        il.Emit(OpCodes.Callvirt, cwtTryGetValue);
        il.Emit(OpCodes.Brtrue, symSealedOrNonExtLabel);
        il.Emit(OpCodes.Ldsfld, runtime.NonExtensibleObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, symStateTmp);
        il.Emit(OpCodes.Callvirt, cwtTryGetValue);
        il.Emit(OpCodes.Brfalse, symRouteLabel); // extensible → store
        il.MarkLabel(symSealedOrNonExtLabel);
        // sealed/non-ext: present symbol → allow update (route); absent → throw.
        var symDictLocal = il.DeclareLocal(_types.DictionaryObjectObject);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, runtime.GetSymbolDictMethod);
        il.Emit(OpCodes.Stloc, symDictLocal);
        il.Emit(OpCodes.Ldloc, symDictLocal);
        il.Emit(OpCodes.Brfalse, symThrowLabel); // no symbol dict → key absent → throw
        il.Emit(OpCodes.Ldloc, symDictLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryObjectObject, "ContainsKey", _types.Object));
        il.Emit(OpCodes.Brfalse, symThrowLabel); // key absent → throw

        il.MarkLabel(symRouteLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, runtime.SetIndex);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(symThrowLabel);
        EmitThrowTypeError(il, runtime, "Cannot assign to a read-only or non-extensible property");

        il.MarkLabel(notSymbolKeyLabel);

        // Check if $Array (for strict mode support)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSArrayType);
        il.Emit(OpCodes.Brtrue, sharpTSArrayLabel);

        // List<object?>
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Brtrue, listLabel);

        // Dictionary
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        // $Object / $TSFunction / System.Type / other receivers: route indexed
        // writes to SetPropertyStrict(obj, ToString(index), value, strictMode),
        // mirroring how the non-strict SetIndex routes them to SetProperty. Pre-fix
        // these fell through to a silent no-op under "use strict", so `child[0] = v`
        // on a class instance / $Object dropped the element — which broke
        // Array.prototype.{reduce,every,filter,indexOf}.call(nonArrayObj, …) on
        // a `new Con()`-style receiver (Test262 Array/prototype/*/15.4.4.*-2-*).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Ret);

        // $Array - call SetStrict with index and strictMode via the long API.
        // Stage E.2 M6: widened from TSArraySetStrict (int) to TSArraySetStrictLong
        // so `"use strict"; arr[2147483648] = v` doesn't truncate to int.MinValue.
        // Parallel to the M3 GetIndex/SetIndex widening.
        il.MarkLabel(sharpTSArrayLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSArrayType);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Convert, "ToInt64", _types.Object));
        il.Emit(OpCodes.Ldarg_2); // value
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Callvirt, runtime.TSArraySetStrictLong);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(listLabel);
        // Check if frozen - in strict mode, throw TypeError
        var listSetLabel = il.DefineLabel();
        var listFrozenCheckLocal = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, listFrozenCheckLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var listNotFrozenLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, listNotFrozenLabel);
        // Frozen - check strictMode and throw if true
        il.Emit(OpCodes.Ldarg_3); // strictMode
        var listFrozenSilentLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, listFrozenSilentLabel);
        EmitThrowTypeError(il, runtime, "Cannot assign to read only property of frozen array");
        il.MarkLabel(listFrozenSilentLabel);
        il.Emit(OpCodes.Ret); // Silently return in non-strict mode
        il.MarkLabel(listNotFrozenLabel);
        // Not frozen - set normally
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.ListOfObjectNullable);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Unbox_Any, _types.Double);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObjectNullable, "set_Item", _types.Int32, _types.Object));
        il.Emit(OpCodes.Ret);

        il.MarkLabel(dictLabel);
        // Dictionary string-key write: route to SetPropertyStrict(obj, ToString(idx),
        // value, strictMode) so PDS accessor setters, non-writable data descriptors,
        // and strict TypeErrors are honored identically to named-property writes.
        // Pre-fix this did a raw dict.set_Item, bypassing the writable/setter checks —
        // so under "use strict" `obj[name] = v` on a writable:false property neither
        // threw nor was suppressed, which propertyHelper.js's isWritable probe (it
        // writes via `obj[name] = …`) read back as "writable". (Test262 Object/
        // defineProperty 15.2.3.6-3-181, defineProperties 15.2.3.7-* et al.)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Object, "ToString"));
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3); // strictMode
        il.Emit(OpCodes.Call, runtime.SetPropertyStrict);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits DeleteProperty(object obj, string name) -> bool
    /// Removes a property from an object and returns true if successful.
    /// Returns false for frozen/sealed objects or if the object doesn't support deletion.
    /// </summary>
    private void EmitDeleteProperty(TypeBuilder typeBuilder, EmittedRuntime runtime)
        => EmitDeletePropertyCore(typeBuilder, runtime, strict: false);

    /// <summary>
    /// Emits DeleteProperty(object obj, string name) -> bool (non-strict) or
    /// DeletePropertyStrict(object obj, string name, bool strictMode) -> bool.
    /// On a failed delete (frozen/sealed object, non-configurable property)
    /// the non-strict variant returns false; the strict variant throws a
    /// TypeError when strictMode is set, else returns false.
    /// The strict variant intentionally has NO $Array or System.Type receiver
    /// branches, and its $TSFunction handler skips the frozen/sealed/PDS
    /// configurability checks — preserved as-is from before the #1131 merge
    /// (behavior-preserving refactor; see the epic notes for the drift list).
    /// </summary>
    private void EmitDeletePropertyCore(TypeBuilder typeBuilder, EmittedRuntime runtime, bool strict)
    {
        var method = typeBuilder.DefineMethod(
            strict ? "DeletePropertyStrict" : "DeleteProperty",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Boolean,
            strict ? [_types.Object, _types.String, _types.Boolean] : [_types.Object, _types.String]
        );
        if (strict)
            runtime.DeletePropertyStrict = method;
        else
            runtime.DeleteProperty = method;

        var il = method.GetILGenerator();
        var nullLabel = il.DefineLabel();
        var dictLabel = il.DefineLabel();
        var trueLabel = il.DefineLabel();

        // Emits the failed-delete path: strict mode (arg 2 set) throws
        // TypeError("Cannot delete property '<name>'<suffix>"), otherwise
        // (non-strict variant, or strictMode == false) returns false.
        void EmitDeleteFail(string suffix)
        {
            if (strict)
            {
                var sloppyLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Brfalse, sloppyLabel);
                EmitThrowTypeErrorWithName(il, runtime, "Cannot delete property '", suffix);
                il.MarkLabel(sloppyLabel);
            }
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
        }

        // null check - return true (deleting from null is allowed in JS)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, trueLabel);

        // Proxy check: uses obj.GetType().FullName comparison (no SharpTS.dll dependency)
        var notProxyLabel = il.DefineLabel();
        EmitProxyDeleteCheck(il, () => il.Emit(OpCodes.Ldarg_0), () => il.Emit(OpCodes.Ldarg_1), notProxyLabel);

        il.MarkLabel(notProxyLabel);

        // Check if $TSObject
        var sharpTSObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSObjectType);
        il.Emit(OpCodes.Brtrue, sharpTSObjectLabel);

        // $TSFunction — `delete fn.name` records in the per-instance set so
        // the synthetic name/length descriptors stop reporting (ECMA-262 §17
        // configurable). Pre-fix this fell through to trueLabel without
        // recording, so verifyProperty's isConfigurable failed.
        var tsFunctionDelLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, runtime.TSFunctionType);
        il.Emit(OpCodes.Brtrue, tsFunctionDelLabel);

        // $Array — `delete arr[i]` turns the slot into a hole. Must come
        // BEFORE the Dictionary check (not relevant here, just ordering)
        // and BEFORE the trueLabel fallthrough so actual deletions happen.
        // (Non-strict only — the strict variant never had an $Array branch.)
        var tsArrayDelLabel = il.DefineLabel();
        if (!strict)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, runtime.TSArrayType);
            il.Emit(OpCodes.Brtrue, tsArrayDelLabel);
        }

        // Dictionary
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, _types.DictionaryStringObject);
        il.Emit(OpCodes.Brtrue, dictLabel);

        if (!strict)
        {
            // System.Type — `delete String.prototype` / `delete Number.MAX_VALUE`.
            // Per ECMA-262 §17 + §22.x: built-in constructor's "prototype" data
            // property is { writable:false, enumerable:false, configurable:false };
            // static constants likewise non-configurable. [[Delete]] returns false
            // on non-configurable. Test262 S15.5.3.1_A3 verifies. PDS check first
            // for user-installed override-descriptors with configurable=true.
            // (Non-strict only — the strict variant never had a Type branch.)
            var notTypeForDelLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Isinst, _types.Type);
            il.Emit(OpCodes.Brfalse, notTypeForDelLabel);
            var typeDelDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, typeDelDescLocal);
            var typeNoPdsDescLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, typeDelDescLocal);
            il.Emit(OpCodes.Brfalse, typeNoPdsDescLabel);
            il.Emit(OpCodes.Ldloc, typeDelDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
            var typeConfigurableLabel = il.DefineLabel();
            il.Emit(OpCodes.Brtrue, typeConfigurableLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(typeConfigurableLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSDeleteProperty);
            il.Emit(OpCodes.Pop);
            // Also mark in the per-Type deletion tracker so the static-names list
            // check in HasOwnPropertyHelper / gOPD doesn't resurrect this name.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.MarkBuiltinDeletedMethod);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(typeNoPdsDescLabel);
            // "prototype"/"name"/"length" are non-configurable on every built-in.
            var typeBuiltinNameTrueLabel = il.DefineLabel();
            void EmitTypeBuiltinNameCheck(string n)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, n);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brtrue, typeBuiltinNameTrueLabel);
            }
            EmitTypeBuiltinNameCheck("prototype");
            EmitTypeBuiltinNameCheck("name");
            EmitTypeBuiltinNameCheck("length");

            // Number Type-specific non-configurable constants. Reflection
            // probe below would miss these because JS names (UPPER_SNAKE_CASE)
            // differ from .NET names (PascalCase): MAX_VALUE → double.MaxValue
            // etc. Without this, `delete Number.MAX_VALUE` returned true
            // (Test262 S15.7.3.2_A3).
            var notNumberTypeForDelLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, _types.Double);
            il.Emit(OpCodes.Call, _types.Type.GetMethod("GetTypeFromHandle")!);
            il.Emit(OpCodes.Bne_Un, notNumberTypeForDelLabel);
            void EmitNumberConstNameCheck(string n)
            {
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, n);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brtrue, typeBuiltinNameTrueLabel);
            }
            EmitNumberConstNameCheck("MAX_VALUE");
            EmitNumberConstNameCheck("MIN_VALUE");
            EmitNumberConstNameCheck("NaN");
            EmitNumberConstNameCheck("POSITIVE_INFINITY");
            EmitNumberConstNameCheck("NEGATIVE_INFINITY");
            EmitNumberConstNameCheck("MAX_SAFE_INTEGER");
            EmitNumberConstNameCheck("MIN_SAFE_INTEGER");
            EmitNumberConstNameCheck("EPSILON");
            il.MarkLabel(notNumberTypeForDelLabel);
            // Object/Array/String constructor static method names: per ECMA-262
            // §17, every other data property has configurable:true. Mark the
            // deletion in the per-Type tracker so subsequent gOPD/hasOwn report
            // the property as absent, then return true. (prototype/name/length
            // and Number constants caught above are non-configurable.)
            var objTypeDelLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldtoken, _types.Object);
            il.Emit(OpCodes.Call, _types.Type.GetMethod("GetTypeFromHandle")!);
            il.Emit(OpCodes.Bne_Un, objTypeDelLabel);
            void EmitObjectMethodDelCheck(string n)
            {
                var skipLabel = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldstr, n);
                il.Emit(OpCodes.Call, _types.GetMethod(_types.String, "op_Equality", _types.String, _types.String));
                il.Emit(OpCodes.Brfalse, skipLabel);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, runtime.MarkBuiltinDeletedMethod);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(skipLabel);
            }
            EmitObjectMethodDelCheck("assign"); EmitObjectMethodDelCheck("create");
            EmitObjectMethodDelCheck("defineProperties"); EmitObjectMethodDelCheck("defineProperty");
            EmitObjectMethodDelCheck("entries"); EmitObjectMethodDelCheck("freeze");
            EmitObjectMethodDelCheck("fromEntries"); EmitObjectMethodDelCheck("getOwnPropertyDescriptor");
            EmitObjectMethodDelCheck("getOwnPropertyDescriptors"); EmitObjectMethodDelCheck("getOwnPropertyNames");
            EmitObjectMethodDelCheck("getOwnPropertySymbols"); EmitObjectMethodDelCheck("getPrototypeOf");
            EmitObjectMethodDelCheck("groupBy"); EmitObjectMethodDelCheck("hasOwn"); EmitObjectMethodDelCheck("is");
            EmitObjectMethodDelCheck("isExtensible"); EmitObjectMethodDelCheck("isFrozen");
            EmitObjectMethodDelCheck("isSealed"); EmitObjectMethodDelCheck("keys");
            EmitObjectMethodDelCheck("preventExtensions"); EmitObjectMethodDelCheck("seal");
            EmitObjectMethodDelCheck("setPrototypeOf"); EmitObjectMethodDelCheck("values");
            il.MarkLabel(objTypeDelLabel);

            // Reflection: any static field/property on the Type → built-in own.
            const System.Reflection.BindingFlags typeDelStaticPub =
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
            var typeDelLocal = il.DeclareLocal(_types.Type);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, _types.Type);
            il.Emit(OpCodes.Stloc, typeDelLocal);
            il.Emit(OpCodes.Ldloc, typeDelLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, (int)typeDelStaticPub);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetField", _types.String, typeof(System.Reflection.BindingFlags)));
            il.Emit(OpCodes.Brtrue, typeBuiltinNameTrueLabel);
            il.Emit(OpCodes.Ldloc, typeDelLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4, (int)typeDelStaticPub);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.Type, "GetProperty", _types.String, typeof(System.Reflection.BindingFlags)));
            il.Emit(OpCodes.Brtrue, typeBuiltinNameTrueLabel);
            // Not a built-in own property — return true (delete-missing = success).
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(typeBuiltinNameTrueLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(notTypeForDelLabel);
        }

        // Other types - cannot delete properties, return true (JS behavior for non-configurable)
        il.Emit(OpCodes.Br, trueLabel);

        // $TSFunction delete handler.
        il.MarkLabel(tsFunctionDelLabel);
        if (strict)
        {
            // Strict variant: record the deletion only — it intentionally skips
            // the frozen/sealed/PDS configurability checks the non-strict
            // handler performs (preserved pre-#1131 behavior).
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.MarkBuiltinDeletedMethod);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
        }
        else
        {
            // Honor configurability:
            //   1. If frozen or sealed (via CWT), return false (silent no-op).
            //   2. If a PDS descriptor exists with configurable=false, return false
            //      without removing.
            //   3. Otherwise: clean up PDS, then mark as deleted in the per-instance
            //      tracker so the synthetic descriptor stops reporting and direct
            //      property lookups return undefined.
            // Frozen check.
            var tsFnDelTmp = il.DeclareLocal(_types.Object);
            il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, tsFnDelTmp);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
            var tsFnNotFrozenLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, tsFnNotFrozenLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);

            // Sealed check.
            il.MarkLabel(tsFnNotFrozenLabel);
            il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, tsFnDelTmp);
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
            var tsFnNotSealedLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, tsFnNotSealedLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);

            // PDS configurable check.
            il.MarkLabel(tsFnNotSealedLabel);
            var tsFnDelDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, tsFnDelDescLocal);
            var tsFnNoPdsLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, tsFnDelDescLocal);
            il.Emit(OpCodes.Brfalse, tsFnNoPdsLabel);
            il.Emit(OpCodes.Ldloc, tsFnDelDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
            var tsFnDelConfigurableLabel = il.DefineLabel();
            il.Emit(OpCodes.Brtrue, tsFnDelConfigurableLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(tsFnDelConfigurableLabel);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSDeleteProperty);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(tsFnNoPdsLabel);

            // Mark as deleted in per-instance tracker (covers synthetic
            // name/length and any other descriptor-less data entry).
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.MarkBuiltinDeletedMethod);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
        }

        // $Array delete handler — if the key is a numeric index call
        // DeleteAt; otherwise return true (JS non-configurable behavior).
        if (!strict)
        {
            il.MarkLabel(tsArrayDelLabel);
            var tsArrDelIndexLocal = il.DeclareLocal(_types.Int64);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, tsArrDelIndexLocal);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Int64, "TryParse", _types.String, _types.Int64.MakeByRefType()));
            var tsArrDelNonNumericLabel = il.DefineLabel();
            il.Emit(OpCodes.Brfalse, tsArrDelNonNumericLabel);

            // arr.DeleteAt(idx); return true;
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, runtime.TSArrayType);
            il.Emit(OpCodes.Ldloc, tsArrDelIndexLocal);
            il.Emit(OpCodes.Callvirt, runtime.TSArrayDeleteAt);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);

            il.MarkLabel(tsArrDelNonNumericLabel);
            // Non-numeric key. PDS-installed named property: honor frozen +
            // descriptor.configurable (mirrors the Dict path's behavior).
            // Pre-fix returned true unconditionally, allowing `delete arr.foo`
            // to silently succeed even when `Object.freeze(arr)` made the
            // property non-configurable.
            var tsArrDelFrozenLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.PDSIsFrozen);
            il.Emit(OpCodes.Brfalse, tsArrDelFrozenLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(tsArrDelFrozenLabel);
            var tsArrDelSealedLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, runtime.PDSIsSealed);
            il.Emit(OpCodes.Brfalse, tsArrDelSealedLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(tsArrDelSealedLabel);
            // Check PDS descriptor configurable.
            var tsArrDelDescLocal = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
            il.Emit(OpCodes.Stloc, tsArrDelDescLocal);
            var tsArrDelNoDescLabel = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, tsArrDelDescLocal);
            il.Emit(OpCodes.Brfalse, tsArrDelNoDescLabel);
            il.Emit(OpCodes.Ldloc, tsArrDelDescLocal);
            il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
            var tsArrDelConfigurableLabel = il.DefineLabel();
            il.Emit(OpCodes.Brtrue, tsArrDelConfigurableLabel);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(tsArrDelConfigurableLabel);
            // Configurable — PDS remove + return true.
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, runtime.PDSDeleteProperty);
            il.Emit(OpCodes.Pop);
            il.MarkLabel(tsArrDelNoDescLabel);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
        }

        // $TSObject - call the DeleteProperty / DeletePropertyStrict instance method
        il.MarkLabel(sharpTSObjectLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, runtime.TSObjectType);
        il.Emit(OpCodes.Ldarg_1);
        if (strict)
        {
            il.Emit(OpCodes.Ldarg_2); // strictMode
            il.Emit(OpCodes.Callvirt, runtime.TSObjectDeletePropertyStrict);
        }
        else
        {
            il.Emit(OpCodes.Callvirt, runtime.TSObjectDeleteProperty);
        }
        il.Emit(OpCodes.Ret);

        // Dictionary - use Remove, honoring frozen/sealed and PDS configurability.
        il.MarkLabel(dictLabel);
        var valueLocal = il.DeclareLocal(_types.Object);

        // Check if frozen
        il.Emit(OpCodes.Ldsfld, runtime.FrozenObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var notFrozenLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notFrozenLabel);

        // Frozen - fail (strict throws / sloppy returns false)
        EmitDeleteFail("' of a frozen object");

        // Check if sealed
        il.MarkLabel(notFrozenLabel);
        il.Emit(OpCodes.Ldsfld, runtime.SealedObjectsField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, valueLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ConditionalWeakTable, "TryGetValue", _types.Object, _types.Object.MakeByRefType()));
        var notSealedLabel = il.DefineLabel();
        il.Emit(OpCodes.Brfalse, notSealedLabel);

        // Sealed - fail (strict throws / sloppy returns false)
        EmitDeleteFail("' of a sealed object");

        // Not frozen/sealed — remove from BOTH the dict (default data entries)
        // AND the PDS descriptor store (Object.defineProperty installs). When
        // a PDS descriptor is present and non-configurable, delete fails per
        // ECMA-262 §10.1.10 without removing: strict throws TypeError
        // (§13.5.1.2), sloppy returns false.
        il.MarkLabel(notSealedLabel);
        var descLocalDel = il.DeclareLocal(runtime.CompiledPropertyDescriptorType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSGetPropertyDescriptor);
        il.Emit(OpCodes.Stloc, descLocalDel);
        var noPdsForDelLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, descLocalDel);
        il.Emit(OpCodes.Brfalse, noPdsForDelLabel);
        // Descriptor present — check Configurable.
        il.Emit(OpCodes.Ldloc, descLocalDel);
        il.Emit(OpCodes.Callvirt, runtime.CompiledPropertyDescriptorConfigurable.GetGetMethod()!);
        var configurableLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, configurableLabel);
        // Non-configurable — fail without removing.
        EmitDeleteFail("' of object");
        il.MarkLabel(configurableLabel);
        // Configurable — remove PDS entry.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, runtime.PDSDeleteProperty);
        il.Emit(OpCodes.Pop);
        il.MarkLabel(noPdsForDelLabel);

        // Always also try to remove from the dict (the property may be a
        // plain data entry without a PDS descriptor). Dictionary.Remove
        // returns false when the key isn't present, which is fine.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, _types.DictionaryStringObject);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "Remove", _types.String));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        // Return true (default for null and other types)
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
    }

    private void EmitDeletePropertyStrict(TypeBuilder typeBuilder, EmittedRuntime runtime)
        => EmitDeletePropertyCore(typeBuilder, runtime, strict: true);

    /// <summary>
    /// Phase 1: Define $MethodCallable type (wraps BuiltInMethod or other callable objects
    /// returned by GetMember so they can be dispatched through InvokeMethodValue/InvokeValue).
    /// </summary>
    internal void EmitMethodCallableTypeDefinition(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
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
        il.Emit(OpCodes.Newobj, _types.ListOfObject.GetConstructor([typeof(IEnumerable<object>)])!);
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

