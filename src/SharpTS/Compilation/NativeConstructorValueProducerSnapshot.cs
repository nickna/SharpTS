using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

/// <summary>The operation that actually emitted a constructor Type value.</summary>
internal enum NativeConstructorValueProducerKind
{
    SourceClass,
    BuiltInConstructor,
    GlobalConstructorProperty,
    ClrConstructor,
}

internal enum NativeConstructorValueClosureKind
{
    NongenericLiteral,
    ExactClosedBclToken,
    UnprovedSourceGenericFamily,
    UnprovedValue,
}

/// <summary>Identity of a real ldtoken/GetTypeFromHandle value emission, not a type annotation.</summary>
internal sealed record NativeConstructorValueProducer(
    Type ExactTokenOwner,
    NativeConstructorValueProducerKind Kind,
    ILGenerator Body,
    int ValueOffset,
    MethodBase? Method,
    Type? LookupOwnerContext);

internal sealed record NativeConstructorValueOwnerSnapshot(
    Type ExactTokenOwner,
    IReadOnlyList<NativeConstructorValueProducer> Producers,
    NativeConstructorValueClosureKind ClosureKind,
    Type? SourceGenericDefinition,
    bool IntrinsicHandled,
    bool PublicConstructorsProved,
    IReadOnlyList<ConstructorInfo> PublicConstructors,
    IReadOnlyList<string> Blockers);

/// <summary>
/// Completed constructor-value evidence shared by independent native consumers.
/// A consumer must prove its own operation; intrinsic construction handling does
/// not prove a frontend constructor scan or generic closure was preempted.
/// </summary>
internal sealed class NativeConstructorValueProducerSnapshot
{
    internal NativeConstructorValueProducerSnapshot(Module module,
        IEnumerable<NativeConstructorValueOwnerSnapshot> owners, IEnumerable<string> blockers)
    {
        Module = module;
        Owners = Array.AsReadOnly(owners.Select(owner => owner with
        {
            Producers = Array.AsReadOnly(owner.Producers.ToArray()),
            PublicConstructors = Array.AsReadOnly(owner.PublicConstructors.ToArray()),
            Blockers = Array.AsReadOnly(owner.Blockers.ToArray()),
        }).ToArray());
        Blockers = Array.AsReadOnly(blockers.ToArray());
    }

    internal Module Module { get; }
    internal IReadOnlyList<NativeConstructorValueOwnerSnapshot> Owners { get; }
    internal IReadOnlyList<string> Blockers { get; }
}
