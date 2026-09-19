using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private readonly record struct DatePrototypePopulateInputs(
        EmittedDescriptorStorageRuntime DescriptorStorage,
        FieldBuilder ObjectPrototypeField,
        MethodBuilder TSFunctionGetOrCreate
    );

    private void DefineDatePrototypePopulateShell(TypeBuilder typeBuilder, EmittedDateRuntime dates)
    {
        dates.PopulatePrototype = typeBuilder.DefineMethod(
            "_DatePrototypePopulate",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            Type.EmptyTypes);
    }

    /// <summary>
    /// Populates <see cref="EmittedDateRuntime.Prototype"/> with <c>$TSFunction</c>
    /// wrappers over the <c>$Runtime.Date*</c> helpers, plus the <c>constructor</c>
    /// back-reference, each installed as a §17 data property (W:T, E:F, C:T).
    /// <para>
    /// A no-op body when the program never mentions <c>Date</c> — the helpers don't exist
    /// then, and neither can a reference to <c>Date.prototype</c>.
    /// </para>
    /// </summary>
    private void EmitDatePrototypePopulate(TypeBuilder typeBuilder, EmittedDateRuntime dates, DatePrototypePopulateInputs inputs)
    {
        var il = dates.PopulatePrototype.GetILGenerator();

        if (dates.Implementation is not { } date)
        {
            il.Emit(OpCodes.Ret);
            dates.MarkPrototypeBodyEmitted();
            return;
        }

        var descriptors = new PrototypeDescriptorInputs(
            inputs.DescriptorStorage.DescriptorConstructor, inputs.DescriptorStorage.DescriptorValue.GetSetMethod()!,
            inputs.DescriptorStorage.DescriptorEnumerable.GetSetMethod()!, inputs.DescriptorStorage.DefineProperty);

        var setItem = _types.GetMethod(_types.DictionaryStringObject, "set_Item",
            _types.String, _types.Object);

        EmitPrototypePopulateGuard(il, dates.Prototype);

        var descLocal = il.DeclareLocal(inputs.DescriptorStorage.DescriptorType);

        // ECMA-262 §21.4.4.1: Date.prototype.constructor is %Date%. Compiled bare `Date`
        // resolves to the emitted $TSDate type, matching GlobalThisStaticEmitter.
        EmitInstallConstructorDescriptor(il, descriptors, dates.Prototype, descLocal, setItem, () =>
        {
            il.Emit(OpCodes.Ldtoken, date.Type);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.Type, "GetTypeFromHandle", _types.RuntimeTypeHandle));
        });

        foreach (var (jsName, helper, jsLength) in DatePrototypeEmitCatalog.Methods)
        {
            EmitWirePrototypeMethodDescriptor(il, descriptors, inputs.TSFunctionGetOrCreate, dates.Prototype, descLocal,
                setItem, jsName, helper(date), jsLength);
        }

        // §21.4.4: Date.prototype's [[Prototype]] is %Object.prototype%.
        il.Emit(OpCodes.Ldsfld, dates.Prototype);
        il.Emit(OpCodes.Ldsfld, inputs.ObjectPrototypeField);
        il.Emit(OpCodes.Call, inputs.DescriptorStorage.SetPrototype);

        il.Emit(OpCodes.Ret);
        dates.MarkPrototypeBodyEmitted();
    }
}
