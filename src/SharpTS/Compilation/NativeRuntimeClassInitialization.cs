using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Late literal initializer dispatch for a completed, nongeneric source-owner inventory.</summary>
internal sealed class NativeRuntimeClassInitialization
{
    internal const string ForeignOwnerMessage = "Initialization of an unproved CLR class owner is not supported in this native guest profile. Use --target dll or --target exe.";
    private readonly TypeProvider _types;
    private readonly TypeBuilder _owner;
    private IReadOnlyList<Type> _owners = [];

    internal NativeRuntimeClassInitialization(ModuleBuilder module, TypeProvider types)
    {
        _types = types;
        _owner = module.DefineType("$NativeRuntimeClassInitialization", TypeAttributes.NotPublic |
            TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        Initialize = _owner.DefineMethod("Initialize", MethodAttributes.Assembly | MethodAttributes.Static,
            types.Void, [types.Type]);
    }

    internal MethodBuilder Initialize { get; }
    internal bool IsComplete { get; private set; }
    internal bool UsesLegacy { get; private set; }
    internal IReadOnlyList<Type> Owners => IsComplete ? _owners :
        throw new InvalidOperationException("Native initializer inventory is incomplete.");

    internal void CompleteEmission(NativeSourceOwnerCatalog catalog, EmittedRuntime runtime,
        NativeClosedSourceFamilySnapshot? families = null)
    {
        if (IsComplete || !catalog.IsComplete)
            throw new InvalidOperationException("Native initialization requires a completed source-owner catalog.");
        if (!ReferenceEquals(catalog.Module, _owner.Module) || !ReferenceEquals(runtime.Errors.Type.Module, _owner.Module))
            throw new InvalidOperationException("Native initialization metadata must belong to this exact output module.");
        var owners = new HashSet<Type>();
        if (families is not null && !ReferenceEquals(families.Module, _owner.Module))
            throw new InvalidOperationException("Closed initializer contexts must belong to this output.");
        UsesLegacy = catalog.Blockers.Count != 0;
        foreach (var intrinsic in IntrinsicOwners(runtime)) AddChain(intrinsic);
        foreach (var declaration in catalog.Implementers)
        {
            if (!declaration.IsCreated() || declaration.Module.ModuleVersionId != _owner.Module.ModuleVersionId)
                throw new InvalidOperationException("Native initializer declarations must belong to this completed output.");
            // A different closed generic instantiation can have independent static state.
            // Keep the complete legacy operation until that domain has its own proof.
            if (families?.ContextsFor(declaration) is { } contexts)
            {
                foreach (var context in contexts) owners.Add(context);
                // These base chains were independently closed from the actual
                // operands at family completion, including persisted TypeSpecs
                // whose live BaseType API is unavailable.
                foreach (var context in families.Owners) owners.Add(context);
            }
            else AddChain(declaration);
        }
        var il = Initialize.GetILGenerator();
        if (UsesLegacy)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Type, "TypeHandle"));
            il.Emit(OpCodes.Call, _types.RuntimeHelpersRunClassConstructor);
            il.Emit(OpCodes.Ret);
        }
        else
        {
            foreach (var type in owners.OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                var next = il.DefineLabel();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldtoken, type);
                il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
                il.Emit(OpCodes.Bne_Un, next);
                il.Emit(OpCodes.Ldtoken, type);
                il.Emit(OpCodes.Call, _types.RuntimeHelpersRunClassConstructor);
                il.Emit(OpCodes.Ret);
                il.MarkLabel(next);
            }
            GuestErrorEmitter.ThrowError(il, runtime.Errors.CreateException, runtime.Errors.TypeErrorConstructor, ForeignOwnerMessage);
        }
        _owner.CreateType();
        _owners = Array.AsReadOnly(owners.ToArray());
        IsComplete = true;

        void AddChain(Type declaration)
        {
            for (Type? current = declaration; current is not null;)
            {
                if (FunctionGenericParameterRegistry.ContainsUnboundType(current)) { UsesLegacy = true; break; }
                owners.Add(current);
                try { current = current.BaseType; }
                catch (NotSupportedException) { UsesLegacy = true; break; }
            }
        }
    }

    // These are constructor identities, not an assumption that framework types
    // have no initializer. Dispatch runs the actual literal type's initializer.
    private IEnumerable<Type> IntrinsicOwners(EmittedRuntime runtime)
    {
        Type?[] owners = [
            _types.Object, _types.String, _types.Double, _types.Boolean, _types.BigInteger,
            _types.IListOfObject, _types.DictionaryObjectObject, _types.HashSetOfObject,
            _types.ConditionalWeakTableObjectObject, _types.WeakReferenceObject, _types.ObjectArray, _types.TaskOfObject,
            runtime.FunctionValues.Type, runtime.Symbols.Type, runtime.Errors.Type,
            runtime.Errors.TypeErrorType, runtime.Errors.RangeErrorType, runtime.Errors.ReferenceErrorType,
            runtime.Errors.SyntaxErrorType, runtime.Errors.URIErrorType, runtime.Errors.EvalErrorType, runtime.Errors.AggregateErrorType,
            runtime.Dates.Implementation?.Type, runtime.RegExps.Implementation?.Type, runtime.Buffer?.Type,
            runtime.MessageChannels.Type, runtime.MessageChannels.Port.Type,
            runtime.TextEncoding?.EncoderType, runtime.TextEncoding?.DecoderType,
            runtime.ArrayBuffer?.Type, runtime.SharedArrayBuffer?.Type, runtime.DataView?.Type,
            runtime.WebStreams?.ReadableType, runtime.WebStreams?.WritableType, runtime.WebStreams?.TransformType,
            runtime.TypedArrays.Implementation?.Int8ArrayType, runtime.TypedArrays.Implementation?.Uint8ArrayType,
            runtime.TypedArrays.Implementation?.Uint8ClampedArrayType, runtime.TypedArrays.Implementation?.Int16ArrayType,
            runtime.TypedArrays.Implementation?.Uint16ArrayType, runtime.TypedArrays.Implementation?.Int32ArrayType,
            runtime.TypedArrays.Implementation?.Uint32ArrayType, runtime.TypedArrays.Implementation?.Float32ArrayType,
            runtime.TypedArrays.Implementation?.Float64ArrayType, runtime.TypedArrays.Implementation?.BigInt64ArrayType,
            runtime.TypedArrays.Implementation?.BigUint64ArrayType
        ];
        return owners.OfType<Type>();
    }
}
