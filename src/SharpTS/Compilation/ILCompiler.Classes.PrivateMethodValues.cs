using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private Stmt.Class ClassExpressionDeclaration(Expr.ClassExpr expression) => new(
        new Token(TokenType.IDENTIFIER, _classExprs.Names[expression], null, 0), expression.TypeParams,
        expression.SuperclassExpr, expression.SuperclassTypeArgs, expression.Methods, expression.Fields,
        expression.Accessors, StaticInitializers: expression.StaticInitializers);

    private void EmitClassExpressionPrivateMethodBodies(Expr.ClassExpr expression, TypeBuilder template)
    {
        string name = _classExprs.Names[expression];
        var declaration = _classes.PrivateElements.Require(name);
        foreach (var value in declaration.MethodValues.Values)
        {
            var original = (value.Source.IsStatic ? declaration.StaticMethods : declaration.Methods)[value.Source.Name.Lexeme.TrimStart('#')];
            var il = original.GetILGenerator();
            if (value.Source.IsStatic) il.Emit(OpCodes.Ldarg_0);
            else
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, EmitterTypeHelpers.SelfFieldReference(_classExprs.DefinitionFields[expression]));
                il.Emit(OpCodes.Castclass, _runtime.ClassDefinitions.Type);
            }
            var definition = il.DeclareLocal(_runtime.ClassDefinitions.Type);
            il.Emit(OpCodes.Stloc, definition);
            if (value.Source.IsStatic) il.Emit(OpCodes.Ldloc, definition);
            else il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloc, definition);
            il.Emit(OpCodes.Ldfld, _runtime.ClassDefinitions.PrivateMembers);
            il.Emit(OpCodes.Ldstr, $"method:{value.Source.Name.Lexeme.TrimStart('#')}");
            il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "get_Item", _types.String));
            il.Emit(OpCodes.Ldc_I4, value.Source.Parameters.Count);
            il.Emit(OpCodes.Newarr, _types.Object);
            for (int i = 0; i < value.Source.Parameters.Count; i++)
            {
                il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4, i); il.Emit(OpCodes.Ldarg, i + 1); il.Emit(OpCodes.Stelem_Ref);
            }
            il.Emit(OpCodes.Call, _runtime.Invocation.Method);
            if (original.ReturnType != _types.Object) il.Emit(OpCodes.Castclass, original.ReturnType);
            il.Emit(OpCodes.Ret);
        }
        EmitPrivateMethodValueBodies(name);
        _classes.PrivateElements.MarkBodiesEmitted(name);
    }

    private readonly Dictionary<MethodBuilder, (string Name, TypeBuilder Owner, Stmt.Class Source)> _privateMethodValueOwners = [];
    private readonly HashSet<Expr.ArrowFunction> _guestReceiverArrows = new(ReferenceEqualityComparer.Instance);

    private sealed class GuestReceiverArrowCollector(HashSet<Expr.ArrowFunction> arrows) : AstVisitorBase
    {
        protected override void VisitArrowFunction(Expr.ArrowFunction expression)
        {
            if (expression.HasOwnThis) return;
            arrows.Add(expression);
            base.VisitArrowFunction(expression);
        }
        protected override void VisitClassExpr(Expr.ClassExpr expression) { }
        protected override void VisitFunction(Stmt.Function statement) { }
    }

    private void DefinePrivateMethodValues(string className, TypeBuilder owner, Stmt.Class declaration, Expr.ClassExpr? expression = null)
    {
        foreach (var source in declaration.Methods.Where(method => method.IsPrivate && method.Body != null))
        {
            var collector = new GuestReceiverArrowCollector(_guestReceiverArrows);
            foreach (var statement in source.Body!) collector.Visit(statement);
            string name = source.Name.Lexeme.TrimStart('#');
            var type = EmitTypeDefinitions.DefineType(_moduleBuilder, $"$PrivateMember_{className}_{name}",
                TypeAttributes.Public | TypeAttributes.Sealed, _types.Object);
            var definition = expression == null ? null : type.DefineField("Definition", _runtime.ClassDefinitions.Type, FieldAttributes.Assembly | FieldAttributes.InitOnly);
            var constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard,
                expression == null ? Type.EmptyTypes : [_runtime.ClassDefinitions.Type]);
            var il = constructor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, _types.GetDefaultConstructor(_types.Object));
            if (definition != null)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Stfld, definition);
            }
            il.Emit(OpCodes.Ret);
            var method = type.DefineMethod("Invoke", MethodAttributes.Public,
                ResolvePrivateMethodReturnType(source, source.IsStatic),
                [_types.Object, .. source.Parameters.Select(parameter => parameter.IsRest ? typeof(List<object>) : _types.Object)]);
            method.DefineParameter(1, ParameterAttributes.None, "__this");
            MarkExpectsThis(method);
            MarkNonConstructible(method);
            MarkPadsUndefined(method);
            MarkFunctionLength(method, source.Parameters);
            MarkFunctionName(method, $"#{name}");
            FieldBuilder? cache = null;
            ConstructorBuilder? initializer = null;
            if (expression == null)
            {
                cache = type.DefineField("Value", _types.Object, FieldAttributes.Assembly | FieldAttributes.Static | FieldAttributes.InitOnly);
                initializer = type.DefineTypeInitializer();
                il = initializer.GetILGenerator();
                il.Emit(OpCodes.Newobj, constructor);
                EmitMethodInfoLiteral(il, method, type);
                il.Emit(OpCodes.Newobj, _runtime.FunctionConstruction.Constructor);
                il.Emit(OpCodes.Stsfld, cache);
                il.Emit(OpCodes.Ret);
            }
            else _classExprs.DefinitionMethods.Add(method, (expression, definition!));
            _classes.PrivateElements.DeclareMethodValue(className, name,
                new PrivateMethodValue(type, constructor, method, cache, initializer, source, definition));
            _privateMethodValueOwners.Add(method, (className, owner, declaration));
        }
    }

    private void EmitPrivateMethodValueBodies(string className)
    {
        foreach (var value in _classes.PrivateElements.Require(className).MethodValues.Values)
        {
            var owner = _privateMethodValueOwners[value.Method];
            EmitGuestMethodValueBody(value.Method, value.Source, () =>
            {
                var ctx = _classExprs.DefinitionMethods.TryGetValue(value.Method, out var evaluated)
                    ? CreateClassExpressionContext(value.Method.GetILGenerator(), evaluated.Expression, owner.Owner, null, value.Method)
                    : CreateModuleMemberContext(value.Method.GetILGenerator(), value.Method);
                ctx.ClassDefinitionOwnerField = value.Definition;
                ApplyPrivateMethodValueContext(ctx, value.Method);
                ctx.EmittingTypeBuilder = value.Type;
                ctx.ArrowEntryPointDCFields = _closures.ArrowEntryPointDCFields.Count > 0 ? _closures.ArrowEntryPointDCFields : null;
                ctx.ArrowFunctionDCFields = _closures.ArrowFunctionDCFields.Count > 0 ? _closures.ArrowFunctionDCFields : null;
                ctx.ArrowScopeDCFields = _closures.ArrowScopeDCFields.Count > 0 ? _closures.ArrowScopeDCFields : null;
                ctx.ArrowScopeDCExtraFieldsByArrow = _arrowScopeDCExtraFields.Count > 0 ? _arrowScopeDCExtraFields : null;
                ApplyCapturedTopLevelVariableAccess(ctx);
                ApplyCommonJsModuleAccess(ctx);
                return ctx;
            }, owner.Name);
            value.Type.CreateType();
        }
    }

    private void ApplyPrivateMethodValueContext(CompilationContext ctx, MethodBuilder method)
    {
        if (!_privateMethodValueOwners.TryGetValue(method, out var owner)) return;
        ctx.CurrentClassName = owner.Name;
        ctx.CurrentClassBuilder = owner.Owner;
        ctx.IsStrictMode = true;
        ctx.IsInstanceMethod = !_classes.PrivateElements.Require(owner.Name).MethodValues.Values.First(value => value.Method == method).Source.IsStatic;
        ctx.GuestThisVariableName = "__this";
        ctx.HasGuestReceiver = true;
        foreach (var parameter in owner.Source.TypeParams ?? []) ctx.GenericTypeParameters[parameter.Name.Lexeme] = _types.Object;
    }

    private void EmitGuestMethodValueBody(MethodBuilder method, Stmt.Function source,
        Func<CompilationContext> createContext, string className)
    {
        var guestThis = new Stmt.Parameter(new Token(TokenType.IDENTIFIER, "__this", null, 0), "any");
        var lowered = source with { Parameters = [guestThis, .. source.Parameters] };
        if (_asyncMethodFunctionDCKeys.TryGetValue(source, out var asyncKey)) _asyncMethodFunctionDCKeys[lowered] = asyncKey;
        if (_generatorMethodFunctionDCKeys.TryGetValue(source, out var generatorKey)) _generatorMethodFunctionDCKeys[lowered] = generatorKey;
        if (_asyncGeneratorMethodFunctionDCKeys.TryGetValue(source, out var asyncGeneratorKey)) _asyncGeneratorMethodFunctionDCKeys[lowered] = asyncGeneratorKey;
        if (source.IsAsync && source.IsGenerator) EmitAsyncGeneratorMethodBody(method, lowered, null, currentClassName: className);
        else if (source.IsAsync) EmitAsyncMethodBody(method, lowered, null, currentClassName: className);
        else if (source.IsGenerator) EmitGeneratorMethodBody(method, lowered, null, currentClassName: className);
        else
        {
            var il = method.GetILGenerator();
            var ctx = createContext();
            ctx.DefineParameter("__this", 1, _types.Object);
            SetupSyncMethodFunctionDisplayClass(ctx, il, source);
            var parameterTypes = method.GetParameters().Skip(1).Select(parameter => parameter.ParameterType).ToArray();
            for (int i = 0; i < source.Parameters.Count; i++) ctx.DefineParameter(source.Parameters[i].Name.Lexeme, i + 2, parameterTypes[i]);
            var emitter = new ILEmitter(ctx);
            EmitFunctionEnvironmentPrologue(il, ctx, emitter, source.Parameters, source.Body, parameterTypes, argumentOffset: 2);
            InitializeSyncMethodCapturedParameters(ctx, il, source, method, argumentOffset: 2);
            foreach (var statement in source.Body ?? []) emitter.EmitStatement(statement);
            if (emitter.HasDeferredReturns) emitter.FinalizeReturns();
            else { EmitDefaultReturnValue(il, method.ReturnType); il.Emit(OpCodes.Ret); }
        }
    }
}
