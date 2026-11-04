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

    private void EmitObservableFieldOrder(ILGenerator il, TypeBuilder owner, LocalBuilder values)
    {
        if (!_observableFieldOwners.Contains(owner)) return;
        var keys = il.DeclareLocal(_types.ListOfObject);
        var result = il.DeclareLocal(_types.DictionaryStringObject);
        var key = il.DeclareLocal(_types.String);
        var value = il.DeclareLocal(_types.Object);
        var index = il.DeclareLocal(_types.Int32);
        var loop = il.DefineLabel(); var next = il.DefineLabel(); var done = il.DefineLabel(); var present = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, _runtime.GuestOwnProperties!.SnapshotKeys); il.Emit(OpCodes.Stloc, keys);
        il.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor); il.Emit(OpCodes.Stloc, result);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldloc, keys);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Count")); il.Emit(OpCodes.Bge, done);
        il.Emit(OpCodes.Ldloc, keys); il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "get_Item", _types.Int32));
        il.Emit(OpCodes.Isinst, _types.String); il.Emit(OpCodes.Stloc, key);
        il.Emit(OpCodes.Ldloc, key); il.Emit(OpCodes.Brfalse, next);
        il.Emit(OpCodes.Ldloc, values); il.Emit(OpCodes.Ldloc, key); il.Emit(OpCodes.Ldloca, value);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "TryGetValue",
            _types.String, _types.Object.MakeByRefType())); il.Emit(OpCodes.Brtrue, present);
        // A base snapshot must not insert placeholders that hide values held
        // in a derived owner's dynamic store. Only the final snapshot adds
        // descriptor-only keys which have no ordinary storage value.
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "GetType"));
        Type self = owner.IsGenericTypeDefinition ? EmitGenerics.MakeGenericType(owner, owner.GetGenericArguments()) : owner;
        il.Emit(OpCodes.Ldtoken, self); il.Emit(OpCodes.Call, _types.TypeGetTypeFromHandle);
        il.Emit(OpCodes.Ceq); il.Emit(OpCodes.Brfalse, next);
        // Descriptor-only properties retain a key without evaluating their getter.
        il.Emit(OpCodes.Ldsfld, _runtime.Sentinels.UndefinedInstance); il.Emit(OpCodes.Stloc, value);
        il.MarkLabel(present);
        il.Emit(OpCodes.Ldloc, result); il.Emit(OpCodes.Ldloc, key); il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);
        il.MarkLabel(next); il.Emit(OpCodes.Ldloc, index); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, loop);
        il.MarkLabel(done); il.Emit(OpCodes.Ldloc, result); il.Emit(OpCodes.Stloc, values);
    }
}
