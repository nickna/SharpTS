using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Compiler-owned late source adapters; independent of the frozen runtime declarations.</summary>
internal sealed class NativeGuestOwnPropertyRegistry(
    EmittedGuestOwnPropertyRuntime runtime, Func<TypeBuilder, bool> isSourceOwner)
{
    private readonly Dictionary<TypeBuilder, MethodBuilder[]> _implementations = new(ReferenceEqualityComparer.Instance);
    internal bool DeclarationsComplete { get; private set; }
    internal bool IsComplete { get; private set; }
    internal int Count => _implementations.Count;

    internal void Register(TypeBuilder owner, MethodBuilder descriptor, MethodBuilder keys, MethodBuilder prototype)
    {
        if (DeclarationsComplete) throw new InvalidOperationException("Guest own-property source declarations are complete.");
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(prototype);
        if (!runtime.IsComplete || !isSourceOwner(owner) || owner.IsCreated() || owner.IsInterface || owner.IsValueType
            || owner.Module.ModuleVersionId != runtime.Contract.Module.ModuleVersionId)
            throw new InvalidOperationException("Own-property adapters require a declared source owner in this compilation.");
        if (_implementations.ContainsKey(owner)) throw new InvalidOperationException("The source owner already has an own-property adapter.");
        MethodBuilder[] targets = [descriptor, keys, prototype];
        MethodInfo[] slots = [runtime.GetOwnDescriptor, runtime.OwnPropertyKeys, runtime.InitialPrototype];
        for (int index = 0; index < targets.Length; index++)
        {
            var target = targets[index];
            var slot = slots[index];
            if (target.DeclaringType != owner || !target.IsPrivate || target.IsStatic || target.IsGenericMethod
                || (target.CallingConvention & CallingConventions.VarArgs) != 0 || target.ReturnType != slot.ReturnType
                || !target.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(slot.GetParameters().Select(parameter => parameter.ParameterType)))
                throw new InvalidOperationException("Own-property targets must be exact erased private instance methods on their source owner.");
        }
        EmitTypeDefinitions.AddInterfaceImplementation(owner, runtime.Contract);
        var methods = new List<MethodBuilder>(targets);
        for (int index = 0; index < targets.Length; index++)
        {
            var slot = slots[index];
            var thunk = owner.DefineMethod("$IGuestOwnProperties." + slot.Name,
                MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final
                    | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                slot.ReturnType, slot.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
            var il = thunk.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            if (index == 0) il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(targets[index]));
            il.Emit(OpCodes.Ret);
            owner.DefineMethodOverride(thunk, slot);
            methods.Add(thunk);
        }
        _implementations.Add(owner, methods.ToArray());
    }

    internal void CompleteDeclarations(IEnumerable<TypeBuilder> expectedOwners)
    {
        if (DeclarationsComplete) throw new InvalidOperationException("Guest own-property source declarations are already complete.");
        ArgumentNullException.ThrowIfNull(expectedOwners);
        var owners = expectedOwners.ToArray();
        var expected = new HashSet<TypeBuilder>(owners, ReferenceEqualityComparer.Instance);
        if (expected.Count != owners.Length || !expected.SetEquals(_implementations.Keys)
            || _implementations.Values.SelectMany(methods => methods).Any(method => method.GetILGenerator().ILOffset == 0))
            throw new InvalidOperationException("The admitted source owner inventory or adapter bodies are incomplete.");
        DeclarationsComplete = true;
    }

    internal void CompleteEmission()
    {
        if (IsComplete) throw new InvalidOperationException("Guest own-property source emission is already complete.");
        if (!DeclarationsComplete || _implementations.Keys.Any(owner => !owner.IsCreated()))
            throw new InvalidOperationException("Guest own-property source types have not all been finalized.");
        IsComplete = true;
    }
}
