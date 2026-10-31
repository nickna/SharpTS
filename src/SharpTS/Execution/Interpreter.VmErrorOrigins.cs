using System.Runtime.CompilerServices;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    // Only parsed VM source supplies these receipts. Values contain no evaluator,
    // context, or scheduler, and weak keys do not retain a discarded script.
    private static ConditionalWeakTable<Expr, VmErrorOrigin>? _vmErrorOrigins;
    private static AsyncLocal<VmErrorOriginState?>? _vmErrorOriginState;
    private sealed record VmErrorOrigin(string Frame);
    private sealed record VmErrorOriginState(Interpreter Owner, string Frame);

    internal static void RecordVmErrorOrigins(List<Stmt> statements,
        SourceDocument document, int lineOffset, int columnOffset)
    {
        var table = LazyInitializer.EnsureInitialized(ref _vmErrorOrigins);
        var visitor = new VmErrorOriginRecorder(table, document, lineOffset, columnOffset);
        foreach (var statement in statements) visitor.Visit(statement);
    }

    private sealed class VmErrorOriginRecorder(
        ConditionalWeakTable<Expr, VmErrorOrigin> table,
        SourceDocument document, int lineOffset, int columnOffset) : AstVisitorBase
    {
        public override void Visit(Expr expression)
        {
            if (expression is Expr.Call or Expr.New && document.PositionOf(expression) is { } position)
            {
                int column = position.StartColumn + (position.StartLine == 1 ? columnOffset : 0);
                table.GetValue(expression, _ => new VmErrorOrigin(
                    $"    at {document.Path}:{position.StartLine + lineOffset}:{column}"));
            }
            base.Visit(expression);
        }
    }

    private VmErrorOriginScope EnterVmErrorOrigin(Expr expression)
    {
        string? next = _vmErrorOrigins?.TryGetValue(expression, out var origin) == true
            ? origin.Frame : null;
        var previous = _vmErrorOriginState?.Value;
        if (next is null && previous is null) return default;
        var state = LazyInitializer.EnsureInitialized(ref _vmErrorOriginState);
        state.Value = next is null ? null : new VmErrorOriginState(this, next);
        return new VmErrorOriginScope(state, previous);
    }

    private readonly struct VmErrorOriginScope(AsyncLocal<VmErrorOriginState?> state, VmErrorOriginState? previous) : IDisposable
    {
        public void Dispose()
        {
            if (state is not null)
                state.Value = previous is { Owner.IsDisposed: false } ? previous : null;
        }
    }

    internal string CreateErrorStack(string header) => _vmErrorOriginState?.Value is { } state && ReferenceEquals(state.Owner, this)
        ? header + "\n" + state.Frame : header;

    internal void RecordVmCreatedError(SharpTSInstance error)
    {
        if (_vmErrorOriginState?.Value is { } state && ReferenceEquals(state.Owner, this))
            VmModuleInterpreter.RecordVmErrorValue(error, this);
    }
}
