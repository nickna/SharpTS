using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private readonly HashSet<TypeBuilder> _observableFieldOwners = new(ReferenceEqualityComparer.Instance);
    private bool _needsObservableFieldLifecycle;

    private void PrepareClassFieldLifecycle(List<Stmt> statements)
    {
        // These registrations and the compact-layout proof depend on checked
        // source, not emitted runtime types. Decide ownership before Phase 1
        // freezes optional runtime metadata in either compilation entry point.
        foreach (var (declaration, type) in _typeMap.ClassDeclarations)
            RegisterLiveSuperConstruction(declaration, type);
        foreach (var (expression, type) in _typeMap.ClassExpressions)
            RegisterLiveSuperConstruction(ClassExpressionDeclaration(expression), type);
        _classes.CompactStorageClasses.UnionWith(
            CompactClassStorageAnalyzer.Analyze(statements, _typeMap, _features, _typeMapper));
        _needsObservableFieldLifecycle = NeedsObservableFieldLifecycle(statements);
    }

    private bool NeedsObservableFieldLifecycle(IEnumerable<Stmt> statements)
    {
        var selection = new FieldLifecycleSelection(_classes.CompactStorageClasses);
        foreach (var statement in statements) selection.Visit(statement);
        return selection.Required;
    }

    private sealed class FieldLifecycleSelection(IReadOnlySet<Stmt.Class> compact) : AstVisitorBase
    {
        internal bool Required { get; private set; }
        protected override void VisitClass(Stmt.Class statement)
        {
            if (statement.IsDeclare) return;
            if (!compact.Contains(statement)) Required = true;
            base.VisitClass(statement);
        }
        protected override void VisitClassExpr(Expr.ClassExpr expression) => Required = true;
    }

    private void EmitObservableFieldWrite(ILGenerator il, TypeBuilder owner, string name, bool definition = false)
    {
        if (!_observableFieldOwners.Contains(owner)) return;
        var done = il.DefineLabel();
        if (!definition)
        {
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Call, _runtime.GuestOwnProperties!.HasOwn);
            il.Emit(OpCodes.Brtrue, done);
        }
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldstr, name);
        il.Emit(OpCodes.Ldstr, owner.FullName + "\0" + name); il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, _runtime.GuestOwnProperties!.CommitDefinition);
        il.MarkLabel(done);
    }

    private void EmitObservableDynamicWrite(ILGenerator il, TypeBuilder owner)
    {
        if (!_observableFieldOwners.Contains(owner)) return;
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, _runtime.GuestOwnProperties!.HasOwn); il.Emit(OpCodes.Brtrue, done);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, _runtime.GuestOwnProperties!.CommitDefinition);
        il.MarkLabel(done);
    }

    private void EmitInactiveFieldBranch(ILGenerator il, TypeBuilder dispatchOwner, GuestDataField field, Label absent)
    {
        if (!_observableFieldOwners.Contains(dispatchOwner)) return;
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldstr, field.SourceName);
        il.Emit(OpCodes.Call, _runtime.GuestOwnProperties!.ActiveSourceField);
        il.Emit(OpCodes.Ldstr, NativeOwnFieldIdentity(field));
        il.Emit(OpCodes.Call, _types.GetMethod(_types.Object, "Equals", _types.Object, _types.Object));
        il.Emit(OpCodes.Brfalse, absent);
    }

}
