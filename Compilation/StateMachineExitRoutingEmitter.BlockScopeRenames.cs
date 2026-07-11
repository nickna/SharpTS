using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class StateMachineExitRoutingEmitter
{
    /// <summary>
    /// Per-binding storage names for block-scoped let/const shadows (#711/#766), supplied by each
    /// family's state analysis. Empty for the common no-shadow case.
    /// </summary>
    protected abstract IReadOnlyDictionary<object, string> BlockScopeRenames { get; }

    /// <summary>Shared empty map for analyses built without the renamer.</summary>
    protected static readonly IReadOnlyDictionary<object, string> NoRenames = new Dictionary<object, string>();

    protected static Token RenameToken(Token original, string lexeme) =>
        new(original.Type, lexeme, original.Literal, original.Line, original.Start);

    // Const declarations, compound/logical assignment, and increment/decrement reach the variable
    // through the operator node's name token (or the increment operand). Rewriting that token to the
    // shadowing binding's storage name before delegating to the base routes both the read and the write
    // to the right field/local (#711/#766). A renamed binding is never a DC/captured name, so the base
    // path (which re-enters each family's EmitVariable / EmitStoreVariable overrides) resolves it as
    // the shadow's own slot.

    protected override void EmitConstDeclaration(Stmt.Const c)
    {
        if (BlockScopeRenames.TryGetValue(c, out var renamed))
            c = c with { Name = RenameToken(c.Name, renamed) };
        base.EmitConstDeclaration(c);
    }

    protected override void EmitCompoundAssign(Expr.CompoundAssign ca)
    {
        if (BlockScopeRenames.TryGetValue(ca, out var renamed))
            ca = ca with { Name = RenameToken(ca.Name, renamed) };
        base.EmitCompoundAssign(ca);
    }

    protected override void EmitLogicalAssign(Expr.LogicalAssign la)
    {
        if (BlockScopeRenames.TryGetValue(la, out var renamed))
            la = la with { Name = RenameToken(la.Name, renamed) };
        base.EmitLogicalAssign(la);
    }

    protected override void EmitPrefixIncrement(Expr.PrefixIncrement pi)
    {
        if (pi.Operand is Expr.Variable v && BlockScopeRenames.TryGetValue(v, out var renamed))
            pi = pi with { Operand = v with { Name = RenameToken(v.Name, renamed) } };
        base.EmitPrefixIncrement(pi);
    }

    protected override void EmitPostfixIncrement(Expr.PostfixIncrement poi)
    {
        if (poi.Operand is Expr.Variable v && BlockScopeRenames.TryGetValue(v, out var renamed))
            poi = poi with { Operand = v with { Name = RenameToken(v.Name, renamed) } };
        base.EmitPostfixIncrement(poi);
    }
}
