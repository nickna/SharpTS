using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public abstract partial class StateMachineExitRoutingEmitter
{
    protected void RegisterInitializedParameterNames(List<Stmt.Parameter> parameters, List<Stmt>? body)
    {
        Ctx.InitializedParameterNames = parameters.Select(parameter => parameter.Name.Lexeme)
            .Concat(ParameterPatternBindings.Declarations(body).Select(binding => binding.Name.Lexeme))
            .ToHashSet(StringComparer.Ordinal);
    }

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
    /// Defaults and lowered pattern bindings are evaluated in declaration order, so a later default
    /// can read every initialized binding of an earlier parameter. Captured defaults also reach the
    /// function display class (#792) when <see cref="ExpressionEmitterBase.GetFunctionDCField"/>
    /// exposes one. The async function wraps this in its defaults-applied resume guard
    /// (<see cref="AsyncMoveNextEmitter"/>); the async arrow runs it on initial entry only.
    /// </summary>
    protected void EmitDefaultParameters(List<Stmt.Parameter> parameters, List<Stmt>? body = null)
    {
        var initializers = Runtime.ParameterBinder.ParameterInitializers(body);
        if (!parameters.Any(p => p.DefaultValue != null) && initializers is null)
            return;

        for (int i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];
            if (param.DefaultValue != null)
                EmitDefault(param, ParameterTdzNames(i));

            if (initializers is null || !initializers.TryGetValue(param.Name.Lexeme, out var initializer))
                continue;

            var previousTdz = Ctx.DefaultParameterTdzNames;
            var pending = ParameterTdzNames(i);
            pending.Remove(param.Name.Lexeme);
            Ctx.DefaultParameterTdzNames = pending;
            try { EmitBinding(initializer, pending); }
            finally { Ctx.DefaultParameterTdzNames = previousTdz; }
            (Ctx.InitializedParameterInitializers ??= new(ReferenceEqualityComparer.Instance)).Add(initializer);
        }

        HashSet<string> ParameterTdzNames(int index)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in parameters.Skip(index))
            {
                names.Add(parameter.Name.Lexeme);
                if (initializers?.TryGetValue(parameter.Name.Lexeme, out var initializer) == true)
                    foreach (var binding in LexicalDeclarationBindings.Collect(initializer.Statements))
                        names.Add(binding.Lexeme);
            }
            return names;
        }

        void EmitBinding(Stmt statement, HashSet<string> pending)
        {
            if (statement is Stmt.Sequence sequence)
            {
                foreach (var child in sequence.Statements) EmitBinding(child, pending);
                return;
            }
            EmitStatement(statement);
            if (statement is Stmt.Var variable) pending.Remove(variable.Name.Lexeme);
            else if (statement is Stmt.Const constant) pending.Remove(constant.Name.Lexeme);
        }

        void EmitDefault(Stmt.Parameter param, HashSet<string> pending)
        {
            var field = GetHoistedVariableField(param.Name.Lexeme);
            if (field == null) return; // Parameter not hoisted (unused in body).

            var applyDefault = IL.DefineLabel();
            var skipDefault = IL.DefineLabel();

            // JavaScript null is an ordinary supplied argument. Only the runtime's
            // $Undefined sentinel (used for both omitted and explicit undefined) fires.
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, field);
            IL.Emit(OpCodes.Isinst, Ctx!.Runtime!.Sentinels.UndefinedType);
            IL.Emit(OpCodes.Brtrue, applyDefault);
            IL.Emit(OpCodes.Br, skipDefault);

            IL.MarkLabel(applyDefault);
            var previousTdz = Ctx.DefaultParameterTdzNames;
            Ctx.DefaultParameterTdzNames = pending;
            try { EmitExpression(param.DefaultValue!); }
            finally { Ctx.DefaultParameterTdzNames = previousTdz; }
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

    protected override string GetClassStorageName(Stmt.Class classStmt) =>
        BlockScopeRenames.TryGetValue(classStmt, out var renamed)
            ? renamed : classStmt.Name.Lexeme;

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
        if (TryInitializeLoopBodyBinding(new Stmt.Var(c.Name, c.TypeAnnotation, c.Initializer))) return;
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
