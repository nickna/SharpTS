using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Strategy interface for emitting IL code for built-in Node.js module methods.
/// Each implementation handles a specific module (fs, path, os, etc.).
/// </summary>
public interface IBuiltInModuleEmitter
{
    /// <summary>
    /// The module name this emitter handles (e.g., "fs", "path", "os").
    /// </summary>
    string ModuleName { get; }

    /// <summary>
    /// Attempts to emit IL for a method call on this module.
    /// </summary>
    /// <param name="emitter">The emitter context for code generation.</param>
    /// <param name="methodName">The name of the method being called.</param>
    /// <param name="arguments">The method arguments.</param>
    /// <returns>True if this emitter handled the method call; false otherwise.</returns>
    bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments);

    /// <summary>
    /// Attempts to emit IL for a property access on this module.
    /// </summary>
    /// <param name="emitter">The emitter context for code generation.</param>
    /// <param name="propertyName">The name of the property being accessed.</param>
    /// <returns>True if this emitter handled the property access; false otherwise.</returns>
    bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName);

    /// <summary>
    /// Gets the list of exported member names for this module.
    /// Used when creating namespace import objects.
    /// </summary>
    IReadOnlyList<string> GetExportedMembers();

    /// <summary>
    /// Checks whether the given member name is an exported property (not a method).
    /// Used by typeof to distinguish properties from methods without emitting IL.
    /// </summary>
    bool IsExportedProperty(string memberName) => false;

    /// <summary>
    /// Members whose reads must be emitted inline at each access site (live) instead of
    /// being read from the import-time namespace-dict snapshot. Opt-in: EmitGet consults
    /// this before its dynamic fallback (cluster.schedulingPolicy, #1170).
    /// </summary>
    bool HasLivePropertyGet(string memberName) => false;

    /// <summary>
    /// Attempts to emit IL for a property assignment on this module
    /// (cluster.schedulingPolicy = x, #1170). Implementations must leave the assigned
    /// value on the stack (assignment-expression semantics).
    /// </summary>
    bool TryEmitPropertySet(IEmitterContext emitter, string propertyName, Expr value) => false;
}
