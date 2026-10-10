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
    LineIndex lines,
    string sourceText,
    bool isLibrary)
{
    internal DebugInfoCollector Collector { get; } = collector;
    internal DebugInfoCollector.SourceFile Document { get; } = document;
    internal SpanTable Spans { get; } = spans;
    internal LineIndex Lines { get; } = lines;

    /// <summary>
    /// True for the bundled stdlib and other documents the user did not write. Their methods are
    /// still described in the PDB — a stack trace should name the line inside them — but they are
    /// marked non-user code so stepping runs through them instead of into them.
    /// </summary>
    internal bool IsLibrary { get; } = isLibrary;

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

        span = ExecutableSpan(statement, span);
        var (startLine, startColumn) = Lines.ToPosition(span.Start);
        var (endLine, endColumn) = Lines.ToPosition(span.End);
        Collector.RecordSequencePoint(method, Document, ilOffset, startLine, startColumn, endLine, endColumn);
    }

    private SourceSpan ExecutableSpan(Stmt statement, SourceSpan span)
    {
        // Overlapping header/body ranges bind body breakpoints to the header before its
        // locals are in scope. Attribute the test only to the source preceding its body.
        Stmt? body = statement switch
        {
            Stmt.If conditional => conditional.ThenBranch,
            Stmt.While loop => loop.Body,
            Stmt.For loop => loop.Body,
            Stmt.ForOf loop => loop.Body,
            Stmt.ForIn loop => loop.Body,
            Stmt.Switch choice => choice.Cases.SelectMany(@case => @case.Body).FirstOrDefault()
                ?? choice.DefaultBody?.FirstOrDefault(),
            _ => null,
        };
        if (body is null || !Spans.TryGetSpan(body, out SourceSpan bodySpan) || bodySpan.IsHidden)
            return span;

        int end = Math.Min(span.End, bodySpan.Start);
        while (end > span.Start && char.IsWhiteSpace(sourceText[end - 1])) end--;
        return end > span.Start ? new SourceSpan(span.Start, end) : span;
    }

    internal void RecordAsyncStep(
        MethodBase method,
        int yieldOffset,
        int resumeOffset) =>
        Collector.RecordAsyncStep(method, yieldOffset, resumeOffset);

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
