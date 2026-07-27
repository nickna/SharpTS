using System.Reflection;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Symbols;

/// <summary>
/// What an emitter needs to attribute the IL it is producing back to TypeScript source: the sink,
/// the document being compiled, that document's statement spans, and its offset-to-position index.
/// </summary>
/// <remarks>
/// One scope exists per source document, and hangs off <see cref="CompilationContext.DebugScope"/>
/// only while <see cref="ILCompiler.EmitDebugSymbols"/> is set. A null scope is the whole of the
/// "non-debug builds pay nothing" story — emitters test one reference and move on.
/// </remarks>
internal sealed class DebugEmitScope(
    DebugInfoCollector collector,
    DebugInfoCollector.SourceFile document,
    SpanTable spans,
    LineIndex lines)
{
    internal DebugInfoCollector Collector { get; } = collector;
    internal DebugInfoCollector.SourceFile Document { get; } = document;
    internal SpanTable Spans { get; } = spans;
    internal LineIndex Lines { get; } = lines;

    /// <summary>
    /// Marks <paramref name="ilOffset"/> in <paramref name="method"/> as the start of
    /// <paramref name="statement"/>.
    /// </summary>
    /// <remarks>
    /// A statement with no recorded span produces nothing. That is deliberate: the alternative —
    /// borrowing a nearby position — would step a debugger onto a line that did not generate the
    /// code, which is worse than not stopping at all. Statements the compiler synthesized carry a
    /// hidden span and become hidden points, so stepping passes through them.
    /// </remarks>
    internal void MarkStatement(MethodBase method, Stmt statement, int ilOffset)
    {
        if (!IsExecutable(statement)) return;
        if (!Spans.TryGetSpan(statement, out var span)) return;

        if (span.IsHidden)
        {
            Collector.RecordHiddenSequencePoint(method, ilOffset);
            return;
        }

        var (startLine, startColumn) = Lines.ToPosition(span.Start);
        var (endLine, endColumn) = Lines.ToPosition(span.End);
        Collector.RecordSequencePoint(method, Document, ilOffset, startLine, startColumn, endLine, endColumn);
    }

    /// <summary>
    /// Whether a statement is one a debugger should be able to stop on.
    /// </summary>
    /// <remarks>
    /// Two kinds are excluded. Type-only declarations produce no code at all. Containers — a block
    /// or the sequence a lowering returns — produce no code *of their own*: they would claim the
    /// same IL offset as the first statement inside them, and since two points cannot share an
    /// offset, the container would take the position and stepping would land on <c>{</c> instead of
    /// the first real statement. Declarations emitted in their own phase are excluded for the same
    /// reason: at this position they contribute no instructions.
    /// </remarks>
    private static bool IsExecutable(Stmt statement) => statement switch
    {
        Stmt.Block or Stmt.Sequence => false,
        // `try {` and `outer:` evaluate nothing themselves — the first statement of the guarded or
        // labeled body starts at the same offset and is the useful place to stop. A conditional or
        // loop header is different: its test really does run there, so it keeps its point.
        Stmt.TryCatch or Stmt.LabeledStatement => false,
        Stmt.Interface or Stmt.TypeAlias => false,
        Stmt.Function or Stmt.Class or Stmt.Enum => false,
        _ => true,
    };
}
