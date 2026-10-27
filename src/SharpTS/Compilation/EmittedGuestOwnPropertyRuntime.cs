using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>Optional frozen declarations for native source-instance own operations.</summary>
public sealed class EmittedGuestOwnPropertyRuntime
{
    internal EmittedGuestOwnPropertyRuntime(Type contract, MethodInfo descriptor, MethodInfo keys, MethodInfo prototype,
        Type descriptorType, Type objectType, Type listType, TypeBuilder stateType, TypeBuilder operationsType,
        IReadOnlyDictionary<string, MethodBuilder> operations, FieldInfo stateOrder, MethodInfo getState)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(prototype);
        ArgumentNullException.ThrowIfNull(operations);
        if (!contract.IsInterface || contract.IsGenericType || !Matches(descriptor, descriptorType, [objectType])
            || !Matches(keys, listType, []) || !Matches(prototype, objectType, [])
            || stateType.Module.ModuleVersionId != contract.Module.ModuleVersionId
            || operationsType.Module.ModuleVersionId != contract.Module.ModuleVersionId
            || stateOrder.DeclaringType != stateType || getState.DeclaringType != operationsType
            || operations.Count != 6 || operations.Any(entry => entry.Key != entry.Value.Name
                || entry.Value.DeclaringType != operationsType || !entry.Value.IsStatic))
            throw new InvalidOperationException("Guest own-property declarations must be exact handles in one emitted module.");
        Contract = contract;
        GetOwnDescriptor = descriptor;
        OwnPropertyKeys = keys;
        InitialPrototype = prototype;
        DescriptorType = descriptorType;
        ObjectType = objectType;
        ListType = listType;
        StateType = stateType;
        OperationsType = operationsType;
        Operations = new System.Collections.ObjectModel.ReadOnlyDictionary<string, MethodBuilder>(operations.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal));
        StateOrder = stateOrder;
        GetState = getState;

        bool Matches(MethodInfo method, Type result, Type[] parameters) =>
            method.DeclaringType?.Module.ModuleVersionId == contract.Module.ModuleVersionId
            && method.DeclaringType?.FullName == contract.FullName && !method.IsStatic && method.IsAbstract
            && method.ReturnType == result
            && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameters);
    }

    public Type Contract { get; }
    public MethodInfo GetOwnDescriptor { get; }
    public MethodInfo OwnPropertyKeys { get; }
    public MethodInfo InitialPrototype { get; }
    public Type DescriptorType { get; }
    internal Type ObjectType { get; }
    internal Type ListType { get; }
    internal TypeBuilder StateType { get; }
    internal TypeBuilder OperationsType { get; }
    internal IReadOnlyDictionary<string, MethodBuilder> Operations { get; }
    internal FieldInfo StateOrder { get; }
    internal MethodInfo GetState { get; }
    /// <summary>Records a successful definition and its active source field; false success is inert. Performs no property operation.</summary>
    public MethodBuilder CommitDefinition => Operations[nameof(CommitDefinition)];
    /// <summary>Checks a successful existing-slot update while retaining its order and active field.</summary>
    public MethodBuilder CommitUpdate => Operations[nameof(CommitUpdate)];
    /// <summary>Removes a successful deletion; failed or absent deletions leave the ledger unchanged.</summary>
    public MethodBuilder CommitDelete => Operations[nameof(CommitDelete)];
    public MethodBuilder HasOwn => Operations[nameof(HasOwn)];
    public MethodBuilder ActiveSourceField => Operations[nameof(ActiveSourceField)];
    /// <summary>Returns a fresh index/string/Symbol key snapshot without reading property values.</summary>
    public MethodBuilder SnapshotKeys => Operations[nameof(SnapshotKeys)];
    public bool IsComplete { get; private set; }

    internal void CompleteEmission()
    {
        if (IsComplete) throw new InvalidOperationException("Guest own-property declarations are already complete.");
        if (!StateType.IsCreated() || !OperationsType.IsCreated()
            || Operations.Values.Any(method => method.GetILGenerator().ILOffset == 0))
            throw new InvalidOperationException("Guest own-property runtime bodies are incomplete.");
        IsComplete = true;
    }
}
