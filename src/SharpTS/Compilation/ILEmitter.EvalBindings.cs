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
    private readonly record struct EvalVariableCell(LocalBuilder Cell);

    internal void InitializeEvalBindingStorage(List<Stmt>? body)
    {
        if (_ctx.ClosureAnalyzer?.GetEvalBodyBindings(body) is not { } bindings) return;
        foreach (string storage in bindings.Values)
        {
            // The outer box exists before any closure is made. Its null value
            // means no eval has instantiated the inner variable cell yet.
            IL.Emit(OpCodes.Ldnull);
            IL.Emit(OpCodes.Newobj, _ctx.Types.StrongBoxOfObjectCtor);
            if (!_resolver.TryStoreVariable(storage))
            {
                var local = IL.DeclareLocal(_ctx.Types.StrongBoxOfObject);
                IL.Emit(OpCodes.Stloc, local);
                _ctx.Locals.RegisterLocal(storage, local);
            }
        }
    }

    private string? GetSharedEvalStorage(string name) =>
        _ctx.ClosureAnalyzer?.GetEvalBindingStorage(name).FirstOrDefault(_resolver.HasVariable);

    private void EmitEvalFallback(Action emit)
    {
        _suppressEvalBinding = true;
        try { emit(); }
        finally { _suppressEvalBinding = false; }
    }

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

    private EvalVariableCell? GetVisibleEvalVarCell(string name)
    {
        if (_suppressEvalBinding || _ctx.Locals.GetNestedScopeLocal(name) is not null) return null;
        var storages = HasOwnEvalVariable(name) ? []
            : _ctx.ClosureAnalyzer?.GetEvalBindingStorage(name).Where(_resolver.HasVariable).ToArray() ?? [];
        if (storages.Length > 0)
        {
            var shared = IL.DeclareLocal(_ctx.Types.StrongBoxOfObject);
            var selected = IL.DefineLabel();
            // Resolve the complete environment chain before evaluating a RHS.
            // An inactive inner eval scope still falls through to an active outer one.
            foreach (string storage in storages)
            {
                _resolver.TryLoadVariable(storage);
                IL.Emit(OpCodes.Castclass, _ctx.Types.StrongBoxOfObject);
                IL.Emit(OpCodes.Ldfld, _ctx.Types.StrongBoxOfObjectValueField);
                IL.Emit(OpCodes.Castclass, _ctx.Types.StrongBoxOfObject);
                IL.Emit(OpCodes.Stloc, shared);
                IL.Emit(OpCodes.Ldloc, shared);
                IL.Emit(OpCodes.Brtrue, selected);
            }
            IL.MarkLabel(selected);
            return new(shared);
        }
        return _evalVarBindings.TryGetValue(name, out var cell) ? new(cell) : null;
    }

    private void InstantiateEvalVar(string name)
    {
        if (GetSharedEvalStorage(name) is { } storage)
        {
            var holder = IL.DeclareLocal(_ctx.Types.StrongBoxOfObject);
            _resolver.TryLoadVariable(storage);
            IL.Emit(OpCodes.Castclass, _ctx.Types.StrongBoxOfObject);
            IL.Emit(OpCodes.Stloc, holder);
            var instantiated = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, holder);
            IL.Emit(OpCodes.Ldfld, _ctx.Types.StrongBoxOfObjectValueField);
            IL.Emit(OpCodes.Brtrue, instantiated);
            IL.Emit(OpCodes.Ldloc, holder);
            IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.Sentinels.UndefinedInstance);
            IL.Emit(OpCodes.Newobj, _ctx.Types.StrongBoxOfObjectCtor);
            IL.Emit(OpCodes.Stfld, _ctx.Types.StrongBoxOfObjectValueField);
            IL.MarkLabel(instantiated);
            return;
        }
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
        IL.Emit(OpCodes.Ldloc, cell.Cell);
        IL.Emit(OpCodes.Brfalse, fallback);
        IL.Emit(OpCodes.Ldloc, cell.Cell);
        IL.Emit(OpCodes.Ldfld, _ctx.Types.StrongBoxOfObjectValueField);
        IL.Emit(OpCodes.Br, done);
        IL.MarkLabel(fallback);
        EmitEvalFallback(() => { EmitVariable(variable); EnsureBoxed(); });
        IL.MarkLabel(done);
        SetStackUnknown();
        return true;
    }

    private bool TryEmitEvalVariableAssignment(Expr.Assign assignment)
    {
        string name = assignment.Name.Lexeme;
        if (GetVisibleEvalVarCell(name) is not { } cell) return false;
        var selected = IL.DeclareLocal(_ctx.Types.StrongBoxOfObject);
        IL.Emit(OpCodes.Ldloc, cell.Cell);
        IL.Emit(OpCodes.Stloc, selected);
        EmitExpression(assignment.Value);
        EnsureBoxed();
        EmitSelectedEvalVariableStore(name, new(selected));
        return true;
    }

    private bool TryEmitEvalVariableTypeOf(Expr.Unary expression)
    {
        if (expression.Right is not Expr.Variable variable
            || GetVisibleEvalVarCell(variable.Name.Lexeme) is not { } cell) return false;
        var fallback = IL.DefineLabel();
        var done = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, cell.Cell);
        IL.Emit(OpCodes.Brfalse, fallback);
        IL.Emit(OpCodes.Ldloc, cell.Cell);
        IL.Emit(OpCodes.Ldfld, _ctx.Types.StrongBoxOfObjectValueField);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.Operators.TypeOf);
        IL.Emit(OpCodes.Br, done);
        IL.MarkLabel(fallback);
        EmitEvalFallback(() => EmitUnary(expression));
        IL.MarkLabel(done);
        SetStackUnknown();
        return true;
    }

    // Consumes a boxed value and leaves the assignment result on the stack.
    private void EmitSelectedEvalVariableStore(string name, EvalVariableCell selected)
    {
        var value = IL.DeclareLocal(_ctx.Types.Object);
        IL.Emit(OpCodes.Stloc, value);
        var fallback = IL.DefineLabel();
        var done = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, selected.Cell);
        IL.Emit(OpCodes.Brfalse, fallback);
        IL.Emit(OpCodes.Ldloc, selected.Cell);
        IL.Emit(OpCodes.Ldloc, value);
        IL.Emit(OpCodes.Stfld, _ctx.Types.StrongBoxOfObjectValueField);
        IL.Emit(OpCodes.Br, done);
        IL.MarkLabel(fallback);
        IL.Emit(OpCodes.Ldloc, value);
        SetStackUnknown();
        EmitEvalFallback(() => EmitStoreAssignmentBindingValue(name));
        IL.MarkLabel(done);
        IL.Emit(OpCodes.Ldloc, value);
        SetStackUnknown();
    }

    private EvalVariableCell SelectEvalVariable(EvalVariableCell cell)
    {
        var selected = IL.DeclareLocal(_ctx.Types.StrongBoxOfObject);
        IL.Emit(OpCodes.Ldloc, cell.Cell);
        IL.Emit(OpCodes.Stloc, selected);
        return new(selected);
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
