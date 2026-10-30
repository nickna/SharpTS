using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Actual own declarations are negative query-coverage obligations, never allocation authority.</summary>
internal sealed class NativeOwnedTypeDeclarations
{
    private static readonly ConditionalWeakTable<ModuleBuilder, NativeOwnedTypeDeclarations> Active = new();
    private readonly ModuleBuilder _module;
    private readonly HashSet<TypeBuilder> _definitions = new(ReferenceEqualityComparer.Instance);
    private string? _closedName;

    private NativeOwnedTypeDeclarations(ModuleBuilder module) => _module = module;

    internal Module Module => _module;
    internal int TrackedDefinitionCount => _definitions.Count;

    internal static NativeOwnedTypeDeclarations Begin(ModuleBuilder module)
    {
        var declarations = new NativeOwnedTypeDeclarations(module);
        Active.Add(module, declarations);
        return declarations;
    }

    internal static TypeBuilder Record(ModuleBuilder module, TypeBuilder definition)
    {
        if (Active.TryGetValue(module, out var declarations)) declarations.Record(definition);
        return definition;
    }

    private void Record(TypeBuilder definition)
    {
        if (!ReferenceEquals(definition.Module, _module))
            throw new InvalidOperationException("Own type declarations must belong to their exact output module.");
        if (_closedName is not null)
        {
            if (CanBeAnInstanceType(definition) && definition.Name == _closedName)
                throw new InvalidOperationException("A relevant own type cannot be defined after native query coverage completes.");
            return;
        }
        if (!_definitions.Add(definition))
            throw new InvalidOperationException("An own type declaration cannot be recorded twice.");
    }

    internal IReadOnlyList<string> CompleteNameCoverage(string name, IReadOnlySet<Type> actualAllocations)
    {
        if (_closedName is not null)
            throw new InvalidOperationException("Own query declaration coverage has already completed.");
        _closedName = name;
        var blockers = new List<string>();
        foreach (var definition in _definitions)
        {
            // The caller performs this exact CLR Name test before the query. A
            // nonmatching declaration cannot enter that operation; it gains no row.
            if (!CanBeAnInstanceType(definition) || definition.Name != name) continue;
            if (!definition.IsCreated() || FunctionGenericParameterRegistry.ContainsUnboundType(definition))
                blockers.Add($"Unproved relevant own declaration: {definition.FullName}");
            else if (!actualAllocations.Contains(definition))
                blockers.Add($"No actual allocation producer for relevant own declaration: {definition.FullName}");
        }
        return blockers.AsReadOnly();
    }

    private static bool CanBeAnInstanceType(TypeBuilder definition) =>
        (definition.Attributes & (TypeAttributes.Abstract | TypeAttributes.Interface)) == 0;
}
