using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class IteratorMoveNextEmitter
{
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

    // The rename-then-delegate overrides for const declarations, compound/logical assignment, and
    // increment/decrement live on StateMachineExitRoutingEmitter (shared with the async families).
}
