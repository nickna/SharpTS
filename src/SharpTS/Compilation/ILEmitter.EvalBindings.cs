using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    // Null means eval has not instantiated this binding yet. A cell preserves
    // the selected reference even when evaluating the RHS executes another eval.
    private readonly Dictionary<string, LocalBuilder> _evalVarBindings = new(StringComparer.Ordinal);
    private readonly HashSet<Stmt> _evalScannedStatements = new(ReferenceEqualityComparer.Instance);
    private bool _suppressEvalBinding;

    private bool HasOwnEvalVariable(string name)
        => _ctx.IsModuleTopLevel || _ctx.TryGetParameter(name, out _)
            || _ctx.Locals.HasLocal(name)
            || _ctx.FunctionDisplayClassLocal is not null
                && _ctx.CapturedFunctionLocals?.Contains(name) == true
            || _ctx.ArrowScopeDisplayClassLocal is not null
                && _ctx.CapturedArrowLocals?.Contains(name) == true;

    private void PrepareEvalVarBindings(Stmt statement)
    {
        if (_ctx.IsStrictMode || _staticEvalCompletionCapture is not null
            || _ctx.StaticDirectEvalStatements is not { Count: > 0 }) return;
        new EvalBindingScanner(this).Visit(statement);
    }

    private sealed class EvalBindingScanner(ILEmitter emitter) : AstVisitorBase
    {
        public override void Visit(Stmt statement)
        {
            if (emitter._evalScannedStatements.Add(statement)) base.Visit(statement);
        }
        protected override void VisitFunction(Stmt.Function statement) { }
        protected override void VisitArrowFunction(Expr.ArrowFunction expression) { }
        protected override void VisitClass(Stmt.Class statement) { }
        protected override void VisitClassExpr(Expr.ClassExpr expression) { }
        protected override void VisitCall(Expr.Call call)
        {
            if (call.Callee is Expr.Variable { Name.Lexeme: "eval" }
                && emitter._ctx.StaticDirectEvalStatements!.TryGetValue(call, out var statements)
                && !DirectivePrologue.HasUseStrict(statements))
                foreach (var (name, _) in Runtime.ParameterBinder.BodyVarDeclarations(statements))
                    if (!emitter.HasOwnEvalVariable(name.Lexeme))
                        emitter.GetEvalVarCell(name.Lexeme);
            base.VisitCall(call);
        }
    }

    private LocalBuilder GetEvalVarCell(string name)
    {
        if (!_evalVarBindings.TryGetValue(name, out var cell))
            _evalVarBindings[name] = cell = IL.DeclareLocal(_ctx.Types.StrongBoxOfObject);
        return cell;
    }

    private LocalBuilder? GetVisibleEvalVarCell(string name)
        => !_suppressEvalBinding && _ctx.Locals.GetNestedScopeLocal(name) is null
            && _evalVarBindings.TryGetValue(name, out var cell) ? cell : null;

    private void InstantiateEvalVar(string name)
    {
        var cell = GetEvalVarCell(name);
        var exists = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, cell);
        IL.Emit(OpCodes.Brtrue, exists);
        IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.Sentinels.UndefinedInstance);
        IL.Emit(OpCodes.Newobj, _ctx.Types.StrongBoxOfObjectCtor);
        IL.Emit(OpCodes.Stloc, cell);
        IL.MarkLabel(exists);
    }

    private bool TryEmitEvalVariableRead(Expr.Variable variable)
    {
        if (GetVisibleEvalVarCell(variable.Name.Lexeme) is not { } cell) return false;
        var fallback = IL.DefineLabel();
        var done = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, cell);
        IL.Emit(OpCodes.Brfalse, fallback);
        IL.Emit(OpCodes.Ldloc, cell);
        IL.Emit(OpCodes.Ldfld, _ctx.Types.StrongBoxOfObjectValueField);
        IL.Emit(OpCodes.Br, done);
        IL.MarkLabel(fallback);
        _suppressEvalBinding = true;
        try { EmitVariable(variable); EnsureBoxed(); }
        finally { _suppressEvalBinding = false; }
        IL.MarkLabel(done);
        SetStackUnknown();
        return true;
    }

    private bool TryEmitEvalVariableAssignment(Expr.Assign assignment)
    {
        string name = assignment.Name.Lexeme;
        if (GetVisibleEvalVarCell(name) is not { } cell) return false;
        var selected = IL.DeclareLocal(_ctx.Types.StrongBoxOfObject);
        IL.Emit(OpCodes.Ldloc, cell);
        IL.Emit(OpCodes.Stloc, selected);
        EmitExpression(assignment.Value);
        EnsureBoxed();
        EmitSelectedEvalVariableStore(name, selected);
        return true;
    }

    private bool TryEmitEvalVariableTypeOf(Expr.Unary expression)
    {
        if (expression.Right is not Expr.Variable variable
            || GetVisibleEvalVarCell(variable.Name.Lexeme) is not { } cell) return false;
        var fallback = IL.DefineLabel();
        var done = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, cell);
        IL.Emit(OpCodes.Brfalse, fallback);
        IL.Emit(OpCodes.Ldloc, cell);
        IL.Emit(OpCodes.Ldfld, _ctx.Types.StrongBoxOfObjectValueField);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.Operators.TypeOf);
        IL.Emit(OpCodes.Br, done);
        IL.MarkLabel(fallback);
        _suppressEvalBinding = true;
        try { EmitUnary(expression); }
        finally { _suppressEvalBinding = false; }
        IL.MarkLabel(done);
        SetStackUnknown();
        return true;
    }

    // Consumes a boxed value and leaves the assignment result on the stack.
    private void EmitSelectedEvalVariableStore(string name, LocalBuilder selected)
    {
        var value = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, value);
        var fallback = IL.DefineLabel();
        var done = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, selected);
        IL.Emit(OpCodes.Brfalse, fallback);
        IL.Emit(OpCodes.Ldloc, selected);
        IL.Emit(OpCodes.Ldloc, value);
        IL.Emit(OpCodes.Stfld, _ctx.Types.StrongBoxOfObjectValueField);
        IL.Emit(OpCodes.Br, done);
        IL.MarkLabel(fallback);
        IL.Emit(OpCodes.Ldloc, value);
        SetStackUnknown();
        _suppressEvalBinding = true;
        try { EmitStoreAssignmentBindingValue(name); }
        finally { _suppressEvalBinding = false; }
        IL.MarkLabel(done);
        IL.Emit(OpCodes.Ldloc, value);
        SetStackUnknown();
    }

    private LocalBuilder SelectEvalVariable(LocalBuilder cell)
    {
        var selected = IL.DeclareLocal(_ctx.Types.StrongBoxOfObject);
        IL.Emit(OpCodes.Ldloc, cell);
        IL.Emit(OpCodes.Stloc, selected);
        return selected;
    }

    private bool TryEmitEvalVariableCompound(Expr.CompoundAssign assignment)
    {
        if (GetVisibleEvalVarCell(assignment.Name.Lexeme) is not { } cell) return false;
        var selected = SelectEvalVariable(cell);
        EmitVariable(new Expr.Variable(assignment.Name));
        EnsureBoxed();
        var current = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, current);
        EmitExpression(assignment.Value);
        EnsureBoxed();
        var value = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, value);
        IL.Emit(OpCodes.Ldloc, current);
        IL.Emit(OpCodes.Ldloc, value);
        _helpers.EmitCompoundOperation(assignment.Operator.Type, _ctx.Runtime!.Operators.Add);
        EmitSelectedEvalVariableStore(assignment.Name.Lexeme, selected);
        return true;
    }

    private bool TryEmitEvalVariableLogical(Expr.LogicalAssign assignment)
    {
        if (GetVisibleEvalVarCell(assignment.Name.Lexeme) is not { } cell) return false;
        var selected = SelectEvalVariable(cell);
        var done = IL.DefineLabel();
        EmitVariable(new Expr.Variable(assignment.Name));
        EnsureBoxed();
        IL.Emit(OpCodes.Dup);
        EmitLogicalConditionCheck(assignment.Operator.Type, done);
        IL.Emit(OpCodes.Pop);
        EmitExpression(assignment.Value);
        EnsureBoxed();
        EmitSelectedEvalVariableStore(assignment.Name.Lexeme, selected);
        IL.MarkLabel(done);
        SetStackUnknown();
        return true;
    }

    private bool TryEmitEvalVariableUpdate(Expr operand, TokenType operation, bool prefix)
    {
        if (operand is not Expr.Variable variable
            || GetVisibleEvalVarCell(variable.Name.Lexeme) is not { } cell) return false;
        var selected = SelectEvalVariable(cell);
        EmitVariable(variable);
        EnsureBoxed();
        var rawValue = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, rawValue);
        EmitNumericUpdate(rawValue, operation == TokenType.PLUS_PLUS, out var oldValue, out var newValue);
        IL.Emit(OpCodes.Ldloc, newValue);
        EmitSelectedEvalVariableStore(variable.Name.Lexeme, selected);
        if (!prefix)
        {
            IL.Emit(OpCodes.Pop);
            IL.Emit(OpCodes.Ldloc, oldValue);
        }
        SetStackUnknown();
        return true;
    }
}
