using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>
/// A compilation-owned callable contract for native guests. The interface carries
/// erased arguments without enumerating constructed generic carrier types.
/// </summary>
public sealed class EmittedNativeCallableRuntime
{
    private readonly Type _objectType;
    private readonly Type _argumentsType;
    private readonly Dictionary<TypeBuilder, (MethodBuilder Target, MethodBuilder Thunk)> _implementations = new(ReferenceEqualityComparer.Instance);

    internal EmittedNativeCallableRuntime(Type contract, MethodInfo invoke, Type objectType, Type argumentsType)
    {
        Contract = contract;
        Invoke = invoke;
        _objectType = objectType;
        _argumentsType = argumentsType;
    }

    public Type Contract { get; }
    public MethodInfo Invoke { get; }
    public bool IsComplete { get; private set; }
    public int CarrierCount => _implementations.Count;
    internal IReadOnlyList<TypeBuilder> Carriers => IsComplete ? Array.AsReadOnly(_implementations.Keys.ToArray()) :
        throw new InvalidOperationException("Native callable carrier metadata is incomplete.");
    public EmittedNativeSourceFunctionRuntime? SourceFunctions { get; private set; }

    internal EmittedNativeSourceFunctionRuntime BeginSourceFunctionsEmission()
    {
        if (IsComplete || SourceFunctions is not null)
            throw new InvalidOperationException("Native source function declarations have already begun or completed.");
        return SourceFunctions = new();
    }

    /// <summary>Registers an exact erased invocation body before its owner is finalized.</summary>
    internal void Register(TypeBuilder carrier, MethodBuilder invoke)
    {
        if (IsComplete) throw new InvalidOperationException("Native callable declarations are complete.");
        ArgumentNullException.ThrowIfNull(carrier);
        ArgumentNullException.ThrowIfNull(invoke);
        if (carrier.Module.ModuleVersionId != Contract.Module.ModuleVersionId || carrier.IsCreated() || carrier.IsInterface || carrier.IsValueType ||
            invoke.DeclaringType != carrier || invoke.IsStatic || invoke.IsGenericMethod ||
            (invoke.CallingConvention & CallingConventions.VarArgs) != 0 || invoke.ReturnType != _objectType ||
            !invoke.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[] { _argumentsType }))
            throw new InvalidOperationException("Native callable bodies must have the carrier-owned object Invoke(object[]) signature.");
        if (_implementations.ContainsKey(carrier))
            throw new InvalidOperationException("The native callable carrier was already registered.");
        EmitTypeDefinitions.AddInterfaceImplementation(carrier, Contract);
        var thunk = carrier.DefineMethod("$INativeCallable.Invoke",
            MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.HideBySig | MethodAttributes.NewSlot, _objectType, [_argumentsType]);
        var il = thunk.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, EmitterTypeHelpers.SelfMethodReference(invoke));
        il.Emit(OpCodes.Ret);
        carrier.DefineMethodOverride(thunk, Invoke);
        _implementations.Add(carrier, (invoke, thunk));
    }

    internal void CompleteEmission()
    {
        if (IsComplete) throw new InvalidOperationException("Native callable declarations are already complete.");
        if (SourceFunctions is { IsComplete: false })
            throw new InvalidOperationException("Native source function declarations are incomplete.");
        if (_implementations.Any(entry => !entry.Key.IsCreated() ||
            entry.Value.Target.GetILGenerator().ILOffset == 0 || entry.Value.Thunk.GetILGenerator().ILOffset == 0))
            throw new InvalidOperationException("Native callable bodies are incomplete.");
        IsComplete = true;
    }
}
