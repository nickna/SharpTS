using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Execution;

/// <summary>
/// Handler registrations for the Interpreter.
/// Configures a NodeRegistry with handlers for all AST node types.
/// </summary>
/// <remarks>
/// This registry provides sync dispatch for expressions and statements,
/// with optional async statement dispatch for statements that need
/// async behavior (e.g., for await...of, try/catch with async body).
/// </remarks>
public static class InterpreterRegistry
{
    /// <summary>
    /// Creates and configures a NodeRegistry for the Interpreter.
    /// Uses reflection-based auto-registration to discover Visit* methods,
    /// with async support enabled for statement dispatch.
    /// </summary>
    /// <returns>A frozen registry ready for dispatch.</returns>
    public static NodeRegistry<Interpreter, object?, ExecutionResult> Create()
    {
        return new NodeRegistry<Interpreter, object?, ExecutionResult>(supportAsync: true)
            .AutoRegister()
            // Register async statement handlers for statements that need async behavior.
            // These handlers use EvaluateAsync and ExecuteAsync internally.
            .RegisterStmtAsync<Stmt.Block>((s, i) => i.ExecuteBlockAsyncVT(s))
            .RegisterStmtAsync<Stmt.Sequence>((s, i) => i.ExecuteSequenceAsyncVT(s))
            .RegisterStmtAsync<Stmt.Expression>((s, i) => i.ExecuteExpressionAsyncVT(s))
            .RegisterStmtAsync<Stmt.If>((s, i) => i.ExecuteIfAsyncVT(s))
            .RegisterStmtAsync<Stmt.While>((s, i) => i.ExecuteWhileAsyncVT(s))
            .RegisterStmtAsync<Stmt.DoWhile>((s, i) => i.ExecuteDoWhileAsyncVT(s))
            .RegisterStmtAsync<Stmt.For>((s, i) => i.ExecuteForAsyncVT(s))
            .RegisterStmtAsync<Stmt.ForOf>((s, i) => i.ExecuteForOfAsyncVT(s))
            .RegisterStmtAsync<Stmt.ForIn>((s, i) => i.ExecuteForInAsyncVT(s))
            .RegisterStmtAsync<Stmt.Switch>((s, i) => i.ExecuteSwitchAsyncVT(s))
            .RegisterStmtAsync<Stmt.TryCatch>((s, i) => i.ExecuteTryCatchAsyncVT(s))
            .RegisterStmtAsync<Stmt.Throw>((s, i) => i.ExecuteThrowAsyncVT(s))
            .RegisterStmtAsync<Stmt.Var>((s, i) => i.ExecuteVarAsyncVT(s))
            .RegisterStmtAsync<Stmt.Const>((s, i) => i.ExecuteConstAsyncVT(s))
            .RegisterStmtAsync<Stmt.Return>((s, i) => i.ExecuteReturnAsyncVT(s))
            .RegisterStmtAsync<Stmt.Print>((s, i) => i.ExecutePrintAsyncVT(s))
            .Freeze();
    }
}
