using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Defines <c>$Arguments : List&lt;object&gt;</c> — a marker subclass used to
    /// represent the JS <c>arguments</c> object in compiled mode. Inherits all
    /// List&lt;object&gt; behavior so existing dispatchers (Isinst List, Castclass
    /// List) continue working transparently. Distinguishable at runtime via
    /// <c>Isinst $Arguments</c> for two ECMA-262 spec checks:
    ///   - <c>Object.prototype.toString.call(arguments) === "[object Arguments]"</c>
    ///   - <c>Array.isArray(arguments) === false</c>
    /// </summary>
    /// <remarks>
    /// Without this marker, <c>arguments</c> bound as a plain List&lt;object&gt;
    /// is indistinguishable from a regular array, and the brand-tagger / IsArray
    /// fall through to the [object Array] / true paths, breaking ~30 test262
    /// tests that probe these spec details on the arguments object.
    /// </remarks>
    internal void EmitArgumentsTypeDefinition(ModuleBuilder moduleBuilder, EmittedRuntime runtime)
    {
        var typeBuilder = moduleBuilder.DefineType(
            "$Arguments",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            _types.ListOfObject
        );
        runtime.ArgumentsType = typeBuilder;

        // public int _length — JS-visible length (per ECMA-262 10.4.4 sloppy
        // arguments objects use ordinary "length" property that does NOT
        // auto-update when arguments[i] = v is set out-of-range). Independent
        // from the underlying List.Count, which DOES grow on out-of-range
        // writes (semver/uuid runtime behavior depends on this).
        var lengthField = typeBuilder.DefineField(
            "_length",
            _types.Int32,
            FieldAttributes.Public);
        runtime.ArgumentsLengthField = lengthField;

        // Ctor: public $Arguments() : base() { _length = 0; }
        var defaultCtor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes
        );
        var defaultIL = defaultCtor.GetILGenerator();
        defaultIL.Emit(OpCodes.Ldarg_0);
        defaultIL.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.ListOfObject));
        defaultIL.Emit(OpCodes.Ldarg_0);
        defaultIL.Emit(OpCodes.Ldc_I4_0);
        defaultIL.Emit(OpCodes.Stfld, lengthField);
        defaultIL.Emit(OpCodes.Ret);
        runtime.ArgumentsDefaultCtor = defaultCtor;

        // Ctor: public $Arguments(int capacity) : base(capacity) { _length = 0; }
        var capacityCtor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.Int32]
        );
        var capIL = capacityCtor.GetILGenerator();
        capIL.Emit(OpCodes.Ldarg_0);
        capIL.Emit(OpCodes.Ldarg_1);
        capIL.Emit(OpCodes.Call, _types.GetConstructor(_types.ListOfObject, _types.Int32));
        capIL.Emit(OpCodes.Ldarg_0);
        capIL.Emit(OpCodes.Ldc_I4_0);
        capIL.Emit(OpCodes.Stfld, lengthField);
        capIL.Emit(OpCodes.Ret);
        _ = capacityCtor;

        // Ctor: public $Arguments(IEnumerable<object> source) : base(source)
        // { _length = base.Count; }  — copies the source and snapshots the
        // count as the JS-visible length.
        var enumCtor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            [_types.IEnumerableOfObject]
        );
        var enumIL = enumCtor.GetILGenerator();
        enumIL.Emit(OpCodes.Ldarg_0);
        enumIL.Emit(OpCodes.Ldarg_1);
        enumIL.Emit(OpCodes.Call, _types.GetConstructor(_types.ListOfObject, _types.IEnumerableOfObject));
        enumIL.Emit(OpCodes.Ldarg_0);
        enumIL.Emit(OpCodes.Ldarg_0);
        enumIL.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.ListOfObject, "Count"));
        enumIL.Emit(OpCodes.Stfld, lengthField);
        enumIL.Emit(OpCodes.Ret);
        runtime.ArgumentsEnumerableCtor = enumCtor;

        typeBuilder.CreateType();
    }
}
