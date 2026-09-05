using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    private Dictionary<LocalBuilder, LocalBuilder> _stringScanSnapshots = [];

    /// <summary>
    /// A promoted accumulator cannot escape or be captured. If a loop never
    /// writes it, one snapshot turns chunked StringBuilder indexing into linear
    /// string traversal. Scope the snapshot to this loop: later appends and
    /// subsequent scans must observe the new contents.
    /// </summary>
    private void EmitStringScanSnapshots(Stmt.For loop)
    {
        var uses = new StringScanUses();
        uses.Visit(loop);
        foreach (string name in uses.Reads.Except(uses.Writes).Order(StringComparer.Ordinal))
        {
            if (_ctx.TryGetPromotedStringAccumulator(name) is not { } builder
                || _stringScanSnapshots.ContainsKey(builder))
                continue;

            var snapshot = IL.DeclareLocal(_ctx.Types.String);
            IL.Emit(OpCodes.Ldloc, builder);
            IL.Emit(OpCodes.Callvirt,
                _ctx.Types.GetMethodNoParams(_ctx.Types.StringBuilder, "ToString"));
            IL.Emit(OpCodes.Stloc, snapshot);
            _stringScanSnapshots.Add(builder, snapshot);
        }
    }

    private sealed class StringScanUses : AstVisitorBase
    {
        public HashSet<string> Reads { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Writes { get; } = new(StringComparer.Ordinal);

        protected override void VisitCall(Expr.Call expr)
        {
            if (expr.Callee is Expr.Get
                {
                    Object: Expr.Variable receiver,
                    Name.Lexeme: "charCodeAt",
                    Optional: false
                })
                Reads.Add(receiver.Name.Lexeme);
            base.VisitCall(expr);
        }

        protected override void VisitAssign(Expr.Assign expr)
        {
            Writes.Add(expr.Name.Lexeme);
            base.VisitAssign(expr);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
        {
            Writes.Add(expr.Name.Lexeme);
            base.VisitCompoundAssign(expr);
        }

        // A loop-local declaration can shadow or reinitialize a binding. Do
        // not snapshot that name, even if a surrounding scope has a builder.
        protected override void VisitVar(Stmt.Var stmt)
        {
            Writes.Add(stmt.Name.Lexeme);
            base.VisitVar(stmt);
        }

        protected override void VisitConst(Stmt.Const stmt)
        {
            Writes.Add(stmt.Name.Lexeme);
            base.VisitConst(stmt);
        }
    }
}
