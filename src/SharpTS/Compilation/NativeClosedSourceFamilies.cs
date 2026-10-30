using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>Construction contexts proved by actual native source operands and invocation selections.</summary>
internal sealed class NativeClosedSourceFamilies(ModuleBuilder module, TypeProvider types)
{
    private readonly List<Invocation> _invocations = [];
    private readonly List<Invocation> _active = [];
    private readonly List<Allocation> _allocations = [];
    private readonly List<(Type Owner, MethodBuilder Stub, int Offset)> _initializers = [];
    private readonly List<string> _unknown = [];
    private NativeClosedSourceFamilySnapshot? _completed;
    private sealed record Allocation(ConstructorInfo Constructor, ILGenerator Body, int Offset, MethodBase? Caller);
    private sealed class Invocation(Expr source, ILGenerator body, MethodBase? caller)
    {
        internal Expr Source { get; } = source;
        internal ILGenerator Body { get; } = body;
        internal MethodBase? Caller { get; } = caller;
        internal bool Selected { get; set; }
    }
    private sealed class Scope(NativeClosedSourceFamilies owner, Invocation invocation) : IDisposable
    {
        public void Dispose()
        {
            if (owner._active.Count == 0 || !ReferenceEquals(owner._active[^1], invocation))
                throw new InvalidOperationException("Native source invocation scopes must close in emission order.");
            owner._active.RemoveAt(owner._active.Count - 1);
        }
    }

    internal bool IsComplete => _completed is not null;
    internal NativeClosedSourceFamilySnapshot Completed => _completed ??
        throw new InvalidOperationException("Native source family contexts have not completed.");

    internal IDisposable? Enter(Expr expression, ILGenerator body, MethodBase? caller)
    {
        EnsureMutable();
        if (expression is not (Expr.New or Expr.Call or Expr.CallPrivate or Expr.TaggedTemplateLiteral)) return null;
        var invocation = new Invocation(expression, body, caller);
        _invocations.Add(invocation); _active.Add(invocation);
        return new Scope(this, invocation);
    }

    internal void SelectNonConstructingCall(Expr.Call source, ILGenerator body)
    {
        EnsureMutable();
        var invocation = _active.LastOrDefault(candidate => ReferenceEquals(candidate.Source, source) && ReferenceEquals(candidate.Body, body));
        if (invocation is null) _unknown.Add("A selected source call has no actual invocation scope");
        else invocation.Selected = true;
    }

    internal static void EmitAllocation(NativeClosedSourceFamilies? families, ILGenerator body,
        ConstructorInfo constructor, MethodBase? caller)
    {
        families?.EnsureMutable();
        int offset = body.ILOffset;
        body.Emit(OpCodes.Newobj, constructor);
        if (families is null) return;
        families._allocations.Add(new(constructor, body, offset, caller));
        var invocation = families._active.LastOrDefault(candidate => candidate.Source is Expr.New &&
            ReferenceEquals(candidate.Body, body) && ReferenceEquals(candidate.Caller, caller));
        if (invocation is not null) invocation.Selected = true;
    }

    internal void RecordInitializerOperand(Type owner, MethodBuilder stub, int offset)
    {
        EnsureMutable();
        if (!ReferenceEquals(stub.Module, module) || offset < 0)
            throw new InvalidOperationException("Native initializer contexts require their actual output stub operand.");
        _initializers.Add((owner, stub, offset));
    }

    internal void RecordUnprovedInitializer(Type owner)
    {
        EnsureMutable();
        _unknown.Add($"Unproved actual initializer context: {owner}");
    }

    internal void CompleteEmission(NativeSourceOwnerCatalog sources, NativeConstructorValueProducerSnapshot values,
        NativeClrAdmission admission)
    {
        EnsureMutable();
        if (_active.Count != 0 || !sources.IsComplete || !ReferenceEquals(sources.Module, module) ||
            !ReferenceEquals(values.Module, module) || admission.CompletedIngress is not { } ingress ||
            !ReferenceEquals(ingress.Module, module))
            throw new InvalidOperationException("Closed source families require completed actual operands and CLR ingress from this output.");
        var blockers = new List<string>(_unknown);
        blockers.AddRange(sources.Blockers);
        foreach (var invocation in _invocations.Where(invocation => !invocation.Selected))
            blockers.Add($"Unclassified actual invocation: {invocation.Source.GetType().Name}");
        // A scalar signature does not certify an arbitrary foreign body's
        // reflection effects on the guest. Unused declarations add no selection.
        if (admission.Selections.Count != 0)
            blockers.Add("Selected foreign CLR operations have no source-family body closure proof");

        var closed = new HashSet<Type>(OperandTypeComparer.Instance);
        var contexts = new Dictionary<Type, HashSet<Type>>();
        foreach (var mapping in sources.CanonicalMappings)
            contexts.Add(mapping.Definition, new(OperandTypeComparer.Instance) { mapping.Canonical });
        foreach (var value in values.Owners.Where(value => value.Producers.Any(producer =>
            producer.Kind == NativeConstructorValueProducerKind.SourceClass)))
        {
            foreach (var producer in value.Producers)
            {
                if (producer.Body.ILOffset <= producer.ValueOffset || producer.Method is { } method && !ReferenceEquals(method.Module, module))
                    throw new InvalidOperationException("Family constructor values require their actual completed output operand.");
                if (producer.Kind == NativeConstructorValueProducerKind.SourceClass && producer.ExactTokenOwner.IsGenericType &&
                    !producer.NormalizesSourceDefinition)
                    blockers.Add($"Unproved source constructor-value normalization: {producer.ExactTokenOwner}");
            }
            AddClosed(value.ExactTokenOwner, "constructor operand");
        }
        foreach (var initializer in _initializers)
        {
            if (initializer.Stub.GetILGenerator().ILOffset <= initializer.Offset)
                throw new InvalidOperationException("Family initializer operands require completed stub bodies.");
            AddClosed(initializer.Owner, "initializer operand");
        }
        foreach (var allocation in _allocations)
        {
            if (allocation.Body.ILOffset <= allocation.Offset || allocation.Constructor.DeclaringType is not { } owner ||
                allocation.Caller is null || !ReferenceEquals(allocation.Caller.Module, module) ||
                !ReferenceEquals(Body(allocation.Caller), allocation.Body))
            {
                blockers.Add("An actual allocation has no completed same-output caller body");
                continue;
            }
            if (!FunctionGenericParameterRegistry.ContainsUnboundType(owner)) AddClosed(owner, "allocation operand");
            else if (!PreservesOwnContext(owner, allocation.Caller))
                blockers.Add($"Unbound actual allocation changes its owner context: {owner}");
        }
        foreach (var family in contexts)
            foreach (var context in family.Value.ToArray()) AddBaseChain(context);
        _completed = new(module, contexts.Select(entry => new NativeClosedSourceFamily(entry.Key,
            Array.AsReadOnly(entry.Value.OrderBy(type => type.ToString(), StringComparer.Ordinal).ToArray()))),
            closed, blockers, _allocations.Select(allocation => new NativeFamilyAllocation(allocation.Constructor,
                allocation.Caller, allocation.Offset, allocation.Body.ILOffset)),
            _initializers.Select(initializer => new NativeFamilyInitializer(initializer.Owner,
                initializer.Stub, initializer.Offset, initializer.Stub.GetILGenerator().ILOffset)),
            _invocations.Select(invocation => new NativeFamilyInvocation(invocation.Source, invocation.Caller,
                invocation.Selected, invocation.Body.ILOffset)), values);

        void AddClosed(Type owner, string role)
        {
            if (FunctionGenericParameterRegistry.ContainsUnboundType(owner)) return;
            Type definition = owner.IsGenericType ? owner.GetGenericTypeDefinition() : owner;
            if (!ReferenceEquals(definition.Module, module)) return;
            if (definition.IsGenericTypeDefinition)
            {
                if (!contexts.TryGetValue(definition, out var family)) blockers.Add($"No completed canonical mapping for {role}: {owner}");
                else family.Add(owner);
            }
            else AddBaseChain(owner);
        }

        void AddBaseChain(Type context)
        {
            for (Type? current = context; current is not null;)
            {
                if (FunctionGenericParameterRegistry.ContainsUnboundType(current))
                { blockers.Add($"Unproved actual base context: {current}"); break; }
                if (!closed.Add(current)) break;
                Type definition = current.IsGenericType ? current.GetGenericTypeDefinition() : current;
                if (!ReferenceEquals(definition.Module, module))
                {
                    if (current != types.Object) blockers.Add($"Foreign base context has no family body proof: {current}");
                    break;
                }
                if (definition is not TypeBuilder builder || !builder.IsCreated() || !sources.Implementers.Contains(builder))
                { blockers.Add($"Unproved actual source owner: {current}"); break; }
                if (contexts.TryGetValue(definition, out var family)) family.Add(current);
                try { current = current.BaseType; }
                catch (NotSupportedException)
                {
                    current = definition.BaseType;
                    if (current is not null && FunctionGenericParameterRegistry.ContainsUnboundType(current))
                    { blockers.Add($"Unproved inherited generic substitution: {context}"); break; }
                }
            }
        }
    }

    private static bool PreservesOwnContext(Type owner, MethodBase caller)
    {
        if (!owner.IsGenericType || caller.DeclaringType is not { IsGenericTypeDefinition: true } context ||
            owner.GetGenericTypeDefinition() != context) return false;
        return owner.GetGenericArguments().SequenceEqual(context.GetGenericArguments());
    }

    private static ILGenerator? Body(MethodBase method) => method switch
    { MethodBuilder builder => builder.GetILGenerator(), ConstructorBuilder constructor => constructor.GetILGenerator(), _ => null };

    // Persisted emit can create distinct TypeSpec objects for the same operand.
    // Compare the real definition and recursively paired argument identities;
    // names cannot establish ownership or generic context.
    private sealed class OperandTypeComparer : IEqualityComparer<Type>
    {
        internal static readonly OperandTypeComparer Instance = new();
        public bool Equals(Type? left, Type? right)
        {
            if (left == right) return true;
            if (left is null || right is null) return false;
            if (left.IsGenericType && right.IsGenericType)
                return left.GetGenericTypeDefinition() == right.GetGenericTypeDefinition() &&
                    left.GetGenericArguments().SequenceEqual(right.GetGenericArguments(), this);
            if (left.IsArray && right.IsArray)
                return left.IsSZArray == right.IsSZArray && left.GetArrayRank() == right.GetArrayRank() &&
                    Equals(left.GetElementType(), right.GetElementType());
            return false;
        }
        public int GetHashCode(Type type) => type.IsGenericType ? type.GetGenericTypeDefinition().GetHashCode() :
            type.IsArray ? GetHashCode(type.GetElementType()!) : type.GetHashCode();
    }

    private void EnsureMutable()
    {
        if (IsComplete) throw new InvalidOperationException("Native source family operands are already complete.");
    }
}

internal sealed record NativeClosedSourceFamily(Type Definition, IReadOnlyList<Type> Contexts);
internal sealed record NativeFamilyAllocation(ConstructorInfo Constructor, MethodBase? Caller, int Offset, int CompletedBodyLength);
internal sealed record NativeFamilyInitializer(Type Owner, MethodBuilder Stub, int Offset, int CompletedBodyLength);
internal sealed record NativeFamilyInvocation(Expr Source, MethodBase? Caller, bool Selected, int CompletedBodyLength);

internal sealed class NativeClosedSourceFamilySnapshot
{
    internal NativeClosedSourceFamilySnapshot(Module module, IEnumerable<NativeClosedSourceFamily> families,
        IEnumerable<Type> owners, IEnumerable<string> blockers, IEnumerable<NativeFamilyAllocation> allocations,
        IEnumerable<NativeFamilyInitializer> initializers, IEnumerable<NativeFamilyInvocation> invocations,
        NativeConstructorValueProducerSnapshot constructorValues)
    {
        Module = module;
        Families = Array.AsReadOnly(families.ToArray());
        Owners = Array.AsReadOnly(owners.OrderBy(type => type.ToString(), StringComparer.Ordinal).ToArray());
        Blockers = Array.AsReadOnly(blockers.ToArray());
        Allocations = Array.AsReadOnly(allocations.ToArray());
        Initializers = Array.AsReadOnly(initializers.ToArray());
        Invocations = Array.AsReadOnly(invocations.ToArray());
        ConstructorValues = constructorValues;
    }
    internal Module Module { get; }
    internal IReadOnlyList<NativeClosedSourceFamily> Families { get; }
    internal IReadOnlyList<Type> Owners { get; }
    internal IReadOnlyList<string> Blockers { get; }
    internal IReadOnlyList<NativeFamilyAllocation> Allocations { get; }
    internal IReadOnlyList<NativeFamilyInitializer> Initializers { get; }
    internal IReadOnlyList<NativeFamilyInvocation> Invocations { get; }
    internal NativeConstructorValueProducerSnapshot ConstructorValues { get; }
    internal bool UsesLegacy => Blockers.Count != 0;
    internal IReadOnlyList<Type>? ContextsFor(Type definition) => UsesLegacy ? null :
        Families.FirstOrDefault(family => family.Definition == definition)?.Contexts;

    internal IEnumerable<Type> Expand(Type owner)
    {
        Type definition = owner.IsGenericType ? owner.GetGenericTypeDefinition() : owner;
        return definition.IsGenericTypeDefinition ? ContextsFor(definition) ?? [owner] : [owner];
    }
}
