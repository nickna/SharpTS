using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class StateMachineExitRoutingEmitter
{
    /// <summary>
    /// Checks whether a variable is a captured-AND-mutated function local routed through the shared
    /// function display class (#674/#725), i.e. the state machine holds a function DC and the name
    /// resolves to one of its fields. Returns false for variables that stay on the by-value snapshot /
    /// hoisted-field path. Each family passes its own function-DC state-machine field.
    /// </summary>
    protected bool TryGetFunctionDCField(FieldBuilder? functionDC, string name, out FieldBuilder dcField)
    {
        dcField = null!;
        return functionDC != null &&
               Ctx.CapturedFunctionLocals?.Contains(name) == true &&
               Ctx.FunctionDisplayClassFields?.TryGetValue(name, out dcField!) == true;
    }

    /// <summary>
    /// Stores the value currently on the stack into <c>this.&lt;&gt;__functionDC.dcField</c>.
    /// <paramref name="leaveValueOnStack"/> re-loads the value afterwards for callers that need to
    /// keep it (the async family mirrors the store into the hoisted field next).
    /// </summary>
    protected void StoreToDCField(FieldBuilder functionDC, FieldBuilder dcField, bool leaveValueOnStack)
    {
        var temp = IL.DeclareLocal(Types.Object);
        IL.Emit(OpCodes.Stloc, temp);
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldfld, functionDC);
        IL.Emit(OpCodes.Ldloc, temp);
        IL.Emit(OpCodes.Stfld, dcField);
        if (leaveValueOnStack)
            IL.Emit(OpCodes.Ldloc, temp);
    }

    /// <summary>
    /// Applies parameter defaults at state-machine entry (#646/#737). A defaulted parameter whose
    /// argument was omitted or explicitly <c>undefined</c> arrives in its hoisted state-machine field
    /// as the <c>$Undefined</c> sentinel; this replaces it with the evaluated default
    /// expression — the state-machine analogue of <see cref="ILEmitter.EmitDefaultParameters"/>.
    /// Defaults are evaluated in declaration order, so a later default may reference an earlier
    /// (already-defaulted) parameter via its field. The generators mirror an applied default into the
    /// function display class (#792) via <see cref="ExpressionEmitterBase.GetFunctionDCField"/>; the
    /// async families leave that virtual at its null default, so the mirror block never fires for
    /// them. The async function wraps this in its defaults-applied resume guard
    /// (<see cref="AsyncMoveNextEmitter"/>); the arrow and generators run it on initial entry only.
    /// </summary>
    protected void EmitDefaultParameters(List<Stmt.Parameter> parameters)
    {
        if (!parameters.Any(p => p.DefaultValue != null))
            return;

        for (int i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];
            if (param.DefaultValue == null) continue;

            var field = GetHoistedVariableField(param.Name.Lexeme);
            if (field == null) continue; // Parameter not hoisted (unused in body) — default is moot.

            var applyDefault = IL.DefineLabel();
            var skipDefault = IL.DefineLabel();

            // JavaScript null is an ordinary supplied argument. Only the runtime's
            // $Undefined sentinel (used for both omitted and explicit undefined) fires.
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, field);
            IL.Emit(OpCodes.Isinst, Ctx!.Runtime!.UndefinedType);
            IL.Emit(OpCodes.Brtrue, applyDefault);
            IL.Emit(OpCodes.Br, skipDefault);

            IL.MarkLabel(applyDefault);
            Ctx.DefaultParameterTdzNames = parameters
                .Skip(i)
                .Select(p => p.Name.Lexeme)
                .ToHashSet(StringComparer.Ordinal);
            EmitExpression(param.DefaultValue);
            Ctx.DefaultParameterTdzNames = null;
            EnsureBoxed();
            var temp = IL.DeclareLocal(Types.Object);
            IL.Emit(OpCodes.Stloc, temp);
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldloc, temp);
            IL.Emit(OpCodes.Stfld, field);

            // A captured-and-mutated parameter's live storage is the function display-class field,
            // not the state-machine field the body no longer reads. Mirror the default into the DC
            // field so a nested arrow observes the default rather than the omitted-arg $Undefined
            // sentinel (#792). Non-captured params return false here and keep the SM-field-only path.
            if (TryGetFunctionDCField(GetFunctionDCField(), param.Name.Lexeme, out var dcField))
            {
                IL.Emit(OpCodes.Ldloc, temp);
                StoreToDCField(GetFunctionDCField()!, dcField, leaveValueOnStack: false);
            }

            IL.MarkLabel(skipDefault);
        }
    }

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
