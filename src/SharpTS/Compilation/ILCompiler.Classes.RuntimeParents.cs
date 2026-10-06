using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private readonly Dictionary<Stmt.Class, Expr.ClassExpr> _runtimeClassDeclarations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TypeBuilder, MethodBuilder> _receiverInitializers = new(ReferenceEqualityComparer.Instance);
    private bool _usesRuntimeParents;

    private bool TryDefineRuntimeClassDeclaration(Stmt.Class declaration)
    {
        if (declaration.SuperclassExpr == null || declaration.IsDeclare) return false;
        var leaf = Expr.GetSuperclassLeafName(declaration.SuperclassExpr);
        if (leaf != null && ((_typeMap.Get(declaration.SuperclassExpr) is not TypeSystem.TypeInfo.Any
            && (_classes.Builders.ContainsKey(GetDefinitionContext().ResolveClassName(leaf)) || _classExprs.VarToClassExpr.ContainsKey(leaf)))
            || Runtime.BuiltIns.BuiltInNames.IsErrorTypeName(leaf)
            || leaf is "Array" or "Promise" or "Map" or "Set" or "WeakMap" or "WeakSet" or "Date" or "RegExp" or "Buffer")) return false;
        if (declaration.SuperclassExpr is Expr.ClassExpr { Methods.Count: 0, Fields.Count: 0 }) return false;
        var expression = new Expr.ClassExpr(declaration.Name, declaration.TypeParams,
            declaration.SuperclassExpr, declaration.SuperclassTypeArgs, declaration.Methods,
            declaration.Fields, declaration.Accessors, declaration.AutoAccessors, declaration.Interfaces,
            declaration.InterfaceTypeArgs, declaration.IsAbstract, declaration.StaticInitializers);
        _runtimeClassDeclarations.Add(declaration, expression);
        _usesRuntimeParents = true;
        if (_typeMap.GetClassType(declaration) is { } checkedType) _typeMap.SetClassExprType(expression, checkedType);
        CollectClassExpression(expression);
        DefineClassExpression(expression);
        _classes.BlockScopedBuilders[declaration] = _classExprs.Builders[expression];
        _classes.DeclarationNames[declaration] = _classExprs.Names[expression];
        _classes.DeclarationNamespaces[declaration] = _currentNamespacePath;
        return true;
    }

    private bool HasRuntimeParent(Expr.ClassExpr expression) => expression.SuperclassExpr != null
        && (_classExprs.Builders[expression].BaseType == null || _classExprs.Builders[expression].BaseType == _types.Object)
        && expression.SuperclassExpr is not Expr.ClassExpr { Methods.Count: 0, Fields.Count: 0 };

    private static bool SupportsReceiverInitialization(Stmt.Class declaration)
    {
        // The finite runtime-parent path is ordinary fields and synchronous
        // methods. Other templates keep their existing CLR construction path.
        if (declaration.TypeParams is { Count: > 0 } || declaration.Accessors is { Count: > 0 }
            || declaration.AutoAccessors is { Count: > 0 }
            || declaration.Fields.Any(field => field.IsPrivate || field.ComputedKey != null)
            || declaration.Methods.Any(method => method.IsPrivate || method.IsAsync || method.IsGenerator
                || method.TypeParams is { Count: > 0 } || method.ComputedKey != null
                || method.Parameters.Any(parameter => parameter.IsParameterProperty || parameter.DestructuredProperties != null))) return false;
        var returns = new ConstructorReplacementVisitor();
        foreach (var statement in declaration.Methods.FirstOrDefault(method => method.Name.Lexeme == "constructor")?.Body ?? [])
            returns.Visit(statement);
        return !returns.HasReplacement;
    }

    private sealed class ConstructorReplacementVisitor : AstVisitorBase
    {
        public bool HasReplacement { get; private set; }
        protected override void VisitReturn(Stmt.Return statement) => HasReplacement |= statement.Value != null;
        protected override void VisitFunction(Stmt.Function statement) { }
        protected override void VisitArrowFunction(Expr.ArrowFunction expression) { }
        protected override void VisitClass(Stmt.Class statement) { }
        protected override void VisitClassExpr(Expr.ClassExpr expression) { }
    }

    private MethodBuilder DefineReceiverInitializer(TypeBuilder owner)
    {
        if (_receiverInitializers.TryGetValue(owner, out var existing)) return existing;
        var method = owner.DefineMethod("$InitializeReceiver", MethodAttributes.Public | MethodAttributes.Static,
            _types.Void, [_types.Object, _types.ObjectArray, _types.Object]);
        _receiverInitializers.Add(owner, method);
        return method;
    }

    private void EmitReceiverInitializer(TypeBuilder owner, Stmt.Class declaration, Expr.ClassExpr? expression = null)
    {
        // These entries deliberately use an explicit guest receiver. A runtime-selected
        // child need not be a CLR subtype of this template.
        var method = DefineReceiverInitializer(owner);
        var il = method.GetILGenerator();
        var ctx = expression == null ? CreateModuleMemberContext(il, method)
            : CreateClassExpressionContext(il, expression, owner, null, method);
        ctx.IsStrictMode = true;
        ctx.CurrentClassBuilder = owner;
        ctx.CurrentClassName = expression == null ? GetQualifiedClassDeclarationName(declaration) : _classExprs.Names[expression];
        ctx.EmittingTypeBuilder = owner;
        ctx.IsInstanceMethod = true;
        ctx.HasGuestReceiver = true;
        ctx.GuestThisVariableName = "__this";
        ctx.ClassDefinitionParameterIndex = 2;
        ctx.DefineParameter("__this", 0, _types.Object);
        ApplyCapturedTopLevelVariableAccess(ctx);
        var emitter = new ILEmitter(ctx);
        var constructor = declaration.Methods.FirstOrDefault(source => source.Body != null && source.Name.Lexeme == "constructor" && !source.IsStatic);
        if (constructor?.Body != null && ReferencesArgumentsIdentifier(constructor.Body)
            && !constructor.Parameters.Any(parameter => parameter.Name.Lexeme == "arguments"))
        {
            var arguments = ctx.Locals.DeclareLocal("arguments", _types.ListOfObject);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Newobj, _runtime.Arguments.EnumerableCtor);
            il.Emit(OpCodes.Stloc, arguments);
        }
        foreach (var (parameter, index) in (constructor?.Parameters ?? []).Select((parameter, index) => (parameter, index)))
        {
            var local = ctx.Locals.DeclareLocal(parameter.Name.Lexeme, _types.Object);
            il.Emit(OpCodes.Ldarg_1);
            if (parameter.IsRest)
            {
                il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ListOfObject, _types.IEnumerableOfObject));
                il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Ldc_I4, index);
                il.Emit(OpCodes.Call, _types.GetMethod(typeof(Math), "Min", _types.Int32, _types.Int32));
                il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4);
                il.Emit(OpCodes.Ldc_I4, index); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Call, _types.GetMethod(typeof(Math), "Max", _types.Int32, _types.Int32));
                il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "GetRange", _types.Int32, _types.Int32));
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4, index);
                il.Emit(OpCodes.Call, _runtime.ClassDefinitions.ReadArgument);
            }
            il.Emit(OpCodes.Stloc, local);
        }
        foreach (var parameter in (constructor?.Parameters ?? []).Where(parameter => parameter.DefaultValue != null))
        {
            var local = ctx.Locals.GetLocal(parameter.Name.Lexeme)!;
            var present = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, local); il.Emit(OpCodes.Isinst, _runtime.Sentinels.UndefinedType); il.Emit(OpCodes.Brfalse, present);
            emitter.EmitExpression(parameter.DefaultValue!); emitter.EmitBoxIfNeeded(parameter.DefaultValue!); il.Emit(OpCodes.Stloc, local);
            il.MarkLabel(present);
        }
        bool explicitSuper = constructor?.Body?.Any(ContainsSuperCall) == true;
        if (!explicitSuper && declaration.SuperclassExpr != null)
        {
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, _runtime.ClassDefinitions.GetParent);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, _runtime.ClassDefinitions.InitializeReceiver);
        }
        bool initialized = false;
        if (!explicitSuper) InitializeFields();
        foreach (var statement in constructor?.Body ?? [])
        {
            emitter.EmitStatement(statement);
            if (!initialized && ContainsSuperCall(statement)) InitializeFields();
        }
        if (!initialized) InitializeFields();
        emitter.FinalizeReturns();
        il.Emit(OpCodes.Ret);

        void InitializeFields()
        {
            initialized = true;
            foreach (var field in declaration.Fields.Where(field => !field.IsStatic && !field.IsDeclare && !field.IsPrivate))
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldstr, field.Name.Lexeme);
                if (field.Initializer != null) { emitter.EmitExpression(field.Initializer); emitter.EmitBoxIfNeeded(field.Initializer); }
                else il.Emit(OpCodes.Ldsfld, _runtime.Sentinels.UndefinedInstance);
                il.Emit(OpCodes.Call, _runtime.ObjectWrite.Index);
            }
        }
    }

    private (TypeBuilder Type, ConstructorBuilder Constructor, MethodBuilder Method)? DefineDeclarationReceiverMethod(
        TypeBuilder owner, MethodBuilder original)
    {
        if (!_usesRuntimeParents) return null;
        var declaration = _classes.Declarations.FirstOrDefault(source => _classes.DeclarationNames.GetValueOrDefault(source) == owner.FullName);
        var source = declaration?.Methods.FirstOrDefault(source => source.Body != null && source.IsStatic == original.IsStatic && source.Name.Lexeme == original.Name);
        if (source == null || !SupportsReceiverInitialization(declaration!)) return null;
        var type = EmitTypeDefinitions.DefineType(_moduleBuilder, $"$ReceiverMember_{owner.FullName}_{original.Name}_{(source.IsStatic ? "Static" : "Instance")}", TypeAttributes.Public | TypeAttributes.Sealed, _types.Object);
        var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, _types.ObjectDefaultCtor); il.Emit(OpCodes.Ret);
        var method = type.DefineMethod("Invoke", MethodAttributes.Public, original.ReturnType,
            [_types.Object, .. source.Parameters.Select(parameter => parameter.IsRest ? _types.ListOfObject : _types.Object)]);
        MarkExpectsThis(method); MarkNonConstructible(method); MarkPadsUndefined(method);
        MarkFunctionLength(method, source.Parameters); MarkFunctionName(method, original.Name);
        EmitGuestMethodValueBody(method, source, () =>
        {
            var ctx = CreateModuleMemberContext(method.GetILGenerator(), method);
            ctx.CurrentClassBuilder = owner; ctx.CurrentClassName = owner.FullName;
            ctx.EmittingTypeBuilder = type; ctx.IsStrictMode = true; ctx.IsInstanceMethod = !source.IsStatic;
            ctx.HasGuestReceiver = true; ctx.GuestThisVariableName = "__this";
            ApplyCapturedTopLevelVariableAccess(ctx);
            return ctx;
        }, owner.FullName!);
        type.CreateType();
        return (type, constructor, method);
    }
}
