using SharpTS.Parsing.Visitors;

namespace SharpTS.Execution;

/// <summary>
/// Handler registrations for the Interpreter.
/// Configures a NodeRegistry with handlers for all AST node types.
/// </summary>
/// <remarks>
/// This registry provides sync dispatch for expressions and statements.
/// Async dispatch continues to use the existing switch-based EvaluateAsync method
/// in Interpreter.Expressions.cs, which may be migrated to registry-based dispatch
/// in a future update.
/// </remarks>
public static class InterpreterRegistry
{
    /// <summary>
    /// Creates and configures a NodeRegistry for the Interpreter.
    /// Uses reflection-based auto-registration to discover Visit* methods.
    /// </summary>
    /// <returns>A frozen registry ready for dispatch.</returns>
    public static NodeRegistry<Interpreter, object?, ExecutionResult> Create()
    {
        return new NodeRegistry<Interpreter, object?, ExecutionResult>()
            .AutoRegister()
            .Freeze();
    }
}
