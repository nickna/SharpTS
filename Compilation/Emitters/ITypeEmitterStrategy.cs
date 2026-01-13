using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Strategy interface for emitting IL code for method calls and property access on specific receiver types.
/// Each implementation handles a specific TypeInfo category (String, Array, Date, etc.).
/// </summary>
public interface ITypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a method call on the receiver.
    /// </summary>
    /// <param name="emitter">The emitter context for code generation.</param>
    /// <param name="receiver">The expression representing the receiver object.</param>
    /// <param name="methodName">The name of the method being called.</param>
    /// <param name="arguments">The method arguments.</param>
    /// <returns>True if this strategy handled the method call; false to try the next strategy.</returns>
    bool TryEmitMethodCall(IEmitterContext emitter, Expr receiver, string methodName, List<Expr> arguments);

    /// <summary>
    /// Attempts to emit IL for a property get on the receiver.
    /// </summary>
    /// <param name="emitter">The emitter context for code generation.</param>
    /// <param name="receiver">The expression representing the receiver object.</param>
    /// <param name="propertyName">The name of the property being accessed.</param>
    /// <returns>True if this strategy handled the property access; false to try the next strategy.</returns>
    bool TryEmitPropertyGet(IEmitterContext emitter, Expr receiver, string propertyName);
}

/// <summary>
/// Strategy interface for emitting IL code for static method calls on specific types.
/// Each implementation handles a specific static type (Math, JSON, Object, etc.).
/// </summary>
public interface IStaticTypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for a static method call.
    /// </summary>
    /// <param name="emitter">The emitter context for code generation.</param>
    /// <param name="methodName">The name of the static method being called.</param>
    /// <param name="arguments">The method arguments.</param>
    /// <returns>True if this strategy handled the method call; false to try the next strategy.</returns>
    bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments);

    /// <summary>
    /// Attempts to emit IL for a static property get.
    /// </summary>
    /// <param name="emitter">The emitter context for code generation.</param>
    /// <param name="propertyName">The name of the static property being accessed.</param>
    /// <returns>True if this strategy handled the property access; false to try the next strategy.</returns>
    bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName);
}
