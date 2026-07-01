using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class IteratorMoveNextEmitter
{
    /// <summary>
    /// Per-binding storage names for block-scoped let/const shadows (#711/#766), supplied by each
    /// family's state analysis. Empty for the common no-shadow case.
    /// </summary>
    protected abstract IReadOnlyDictionary<object, string> BlockScopeRenames { get; }

    protected static Token RenameToken(Token original, string lexeme) =>
        new(original.Type, lexeme, original.Literal, original.Line, original.Start);

    /// <summary>
    /// Routes reads and writes of a captured-AND-mutated iterator local/parameter through the shared
    /// function display class (#674/#725) so a write inside an arrow/callback and the generator body
    /// observe the same storage. Both iterator state machines are reference types, so the DC — held by
    /// the <c>&lt;&gt;__functionDC</c> state-machine field — survives across yields (and, for the async
    /// generator, awaits) and is the single source of truth. Returns false (no DC routing) for variables
    /// that stay on the existing by-value snapshot / hoisted-field path.
    /// </summary>
    protected bool TryGetFunctionDCField(string name, out FieldBuilder dcField)
    {
        dcField = null!;
        return GetFunctionDCField() != null &&
               Ctx.CapturedFunctionLocals?.Contains(name) == true &&
               Ctx.FunctionDisplayClassFields?.TryGetValue(name, out dcField!) == true;
    }

    /// <summary>
    /// Stores the value currently on the stack into <c>this.&lt;&gt;__functionDC.dcField</c>, consuming
    /// it. Caller is responsible for any duplicate it wants to keep as a result.
    /// </summary>
    protected void StoreToDCField(FieldBuilder dcField)
    {
        var temp = IL.DeclareLocal(Types.Object);
        IL.Emit(OpCodes.Stloc, temp);
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldfld, GetFunctionDCField()!);
        IL.Emit(OpCodes.Ldloc, temp);
        IL.Emit(OpCodes.Stfld, dcField);
    }

    protected override void EmitVariable(Expr.Variable v)
    {
        // Resolve a shadowing block-scoped binding to its own storage before any DC routing (#711/#766);
        // a renamed binding is never a captured/DC name, so the DC check below correctly falls through.
        if (BlockScopeRenames.TryGetValue(v, out var renamed))
            v = v with { Name = RenameToken(v.Name, renamed) };

        if (TryGetFunctionDCField(v.Name.Lexeme, out var dcField))
        {
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, GetFunctionDCField()!);
            IL.Emit(OpCodes.Ldfld, dcField);
            SetStackUnknown();
            return;
        }

        base.EmitVariable(v);
    }

    protected override void EmitVarDeclaration(Stmt.Var v)
    {
        if (BlockScopeRenames.TryGetValue(v, out var renamed))
            v = v with { Name = RenameToken(v.Name, renamed) };

        if (TryGetFunctionDCField(v.Name.Lexeme, out var dcField))
        {
            if (v.Initializer != null)
            {
                EmitExpression(v.Initializer);
                EnsureBoxed();
            }
            else
            {
                IL.Emit(OpCodes.Ldsfld, Ctx.Runtime!.UndefinedInstance);
            }
            StoreToDCField(dcField);
            return;
        }

        base.EmitVarDeclaration(v);
    }

    protected override void EmitAssign(Expr.Assign a)
    {
        if (BlockScopeRenames.TryGetValue(a, out var renamed))
            a = a with { Name = RenameToken(a.Name, renamed) };

        if (TryGetFunctionDCField(a.Name.Lexeme, out var dcField))
        {
            EmitExpression(a.Value);
            EnsureBoxed();
            IL.Emit(OpCodes.Dup); // keep a copy as the assignment expression's result
            StoreToDCField(dcField);
            SetStackUnknown();
            return;
        }

        base.EmitAssign(a);
    }

    /// <summary>
    /// Store side of compound assignment, logical assignment, and increment/decrement (the value is
    /// already on the stack). Reads for those operators go through <see cref="EmitVariable"/>, so
    /// routing the store here keeps both ends on the function DC.
    /// </summary>
    protected override void EmitStoreVariable(string name)
    {
        if (TryGetFunctionDCField(name, out var dcField))
        {
            StoreToDCField(dcField);
            return;
        }

        base.EmitStoreVariable(name);
    }

    // Const declarations, compound/logical assignment, and increment/decrement reach the variable
    // through the operator node's name token (or the increment operand). Rewriting that token to the
    // shadowing binding's storage name before delegating to the base routes both the read and the write
    // to the right field/local (#711/#766). A renamed binding is never a DC/captured name, so the base
    // path (which re-enters EmitVariable / EmitStoreVariable) resolves it as a plain slot.

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
