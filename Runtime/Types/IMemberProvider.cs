namespace SharpTS.Runtime.Types;

/// <summary>
/// The interpreter member-dispatch contract: resolve a named member (method or
/// property) on a runtime value, returning <c>null</c> when the member is absent.
/// </summary>
/// <remarks>
/// Implemented by <see cref="SharpTSEventEmitter"/> (and therefore its whole family:
/// Readable/Writable/Duplex/Socket/Http*/etc.). Before #1139 the base
/// <c>GetMember</c> was neither virtual nor an interface member, so ~30 subclasses
/// declared <c>public new object? GetMember</c> and lost polymorphism — a base-typed
/// reference resolved members against the static type, not the runtime type. Making
/// the base method virtual and the shadows overrides restores normal dispatch and
/// makes the compiled-reflection fallback unambiguous.
/// </remarks>
public interface IMemberProvider
{
    /// <summary>Resolves the named member, or returns <c>null</c> if it does not exist.</summary>
    object? GetMember(string name);
}
