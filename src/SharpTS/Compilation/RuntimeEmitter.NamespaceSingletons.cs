using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Value-position namespace singletons for AbortSignal and Intl (#224).
    ///
    /// Direct forms (`AbortSignal.abort(x)`, `new Intl.NumberFormat(...)`) are
    /// intercepted at compile time by AbortSignalStaticEmitter /
    /// TryEmitIntlConstructor and never touch these. The singletons only
    /// surface when the namespace is used as a VALUE — bare reference,
    /// aliasing (`const I = Intl`), `typeof`, passing as an argument — the
    /// same role MathSingletonField/JsonSingletonField play for Math/JSON.
    /// Members are $TSFunction wrappers over the existing $Runtime helpers, so
    /// aliased calls (`A.abort('r')`) and aliased construction
    /// (`new I.NumberFormat(...)` via NewOnFunction + the IsConstructor
    /// CreateIntl* exemption) reuse one implementation.
    ///
    /// Lazily populated: the populate shell is called before each bare-
    /// reference load; the field doubles as the populated flag.
    /// </summary>
    /// <summary>
    /// Pre-defines the namespace singleton fields so earlier $Runtime methods
    /// can reference them — InstanceOf brand-checks the AbortSignal singleton
    /// (#246) and is emitted before the populate bodies (which need the
    /// AbortSignal*/CreateIntl* helpers emitted later in EmitRuntimeClass).
    /// </summary>
    private void DefineNamespaceSingletonFields(TypeBuilder typeBuilder, EmittedAbortRuntime? abort, EmittedIntlRuntime? intl)
    {
        if (abort is not null)
            abort.NamespaceField = DefineNamespaceSingletonField(typeBuilder, "AbortSignal");
        if (intl is not null)
            intl.NamespaceField = DefineNamespaceSingletonField(typeBuilder, "Intl");
    }

    private FieldBuilder DefineNamespaceSingletonField(TypeBuilder typeBuilder, string namespaceName) =>
        typeBuilder.DefineField(
            $"_{namespaceName}Namespace",
            _types.DictionaryStringObject,
            FieldAttributes.Public | FieldAttributes.Static);

    private void EmitNamespaceSingletons(
        TypeBuilder typeBuilder, EmittedAbortRuntime? abort, EmittedIntlRuntime? intl,
        MethodBuilder functionGetOrCreate)
    {
        if (abort is not null)
        {
            abort.NamespacePopulate =
                EmitNamespaceSingleton(typeBuilder, functionGetOrCreate, "AbortSignal", abort.NamespaceField,
                [
                    ("abort", abort.SignalAbort, 1),
                    ("timeout", abort.SignalTimeout, 1),
                    ("any", abort.SignalAny, 1),
                ]);
        }

        if (intl is not null)
        {
            intl.NamespacePopulate =
                EmitNamespaceSingleton(typeBuilder, functionGetOrCreate, "Intl", intl.NamespaceField,
                [
                    ("NumberFormat", intl.CreateNumberFormat, 2),
                    ("DateTimeFormat", intl.CreateDateTimeFormat, 2),
                    ("Collator", intl.CreateCollator, 2),
                    ("PluralRules", intl.CreatePluralRules, 2),
                    ("RelativeTimeFormat", intl.CreateRelativeTimeFormat, 2),
                    ("ListFormat", intl.CreateListFormat, 2),
                    ("Segmenter", intl.CreateSegmenter, 2),
                    ("DisplayNames", intl.CreateDisplayNames, 2),
                ]);
        }
    }

    private MethodBuilder EmitNamespaceSingleton(
        TypeBuilder typeBuilder,
        MethodBuilder functionGetOrCreate,
        string namespaceName,
        FieldBuilder field,
        (string JsName, MethodBuilder Helper, int JsLength)[] members)
    {
        var method = typeBuilder.DefineMethod(
            $"_{namespaceName}NamespacePopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);

        var il = method.GetILGenerator();
        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        // Already populated? (field doubles as the flag)
        var doFillLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldsfld, field);
        il.Emit(OpCodes.Brfalse, doFillLabel);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(doFillLabel);

        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, dictLocal);

        foreach (var (jsName, helper, jsLength) in members)
        {
            // dict[jsName] = $TSFunction.GetOrCreate(helper, jsName, jsLength)
            // — GetOrCreate keys on MethodInfo so the wrapper identity is
            // shared with any other path exposing the same helper.
            il.Emit(OpCodes.Ldloc, dictLocal);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Ldtoken, helper);
            il.Emit(OpCodes.Call, _types.MethodBaseGetMethodFromHandle);
            il.Emit(OpCodes.Castclass, _types.MethodInfo);
            il.Emit(OpCodes.Ldstr, jsName);
            il.Emit(OpCodes.Ldc_I4, jsLength);
            il.Emit(OpCodes.Call, functionGetOrCreate);
            il.Emit(OpCodes.Callvirt, setItem);
        }

        // Publish last so a half-filled dict is never observable.
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Stsfld, field);
        il.Emit(OpCodes.Ret);

        return method;
    }
}
