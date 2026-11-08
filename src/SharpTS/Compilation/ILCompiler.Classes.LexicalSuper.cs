using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private readonly Dictionary<Expr.ClassExpr, MethodBuilder> _lexicalSuperElementInitializers = new(ReferenceEqualityComparer.Instance);

    private void DeclareLexicalSuperElementInitializer(Expr.ClassExpr expression, TypeBuilder owner)
    {
        var declaration = ClassExpressionDeclaration(expression);
        bool ownElements = expression.Fields.Any(field => !field.IsStatic && !field.IsDeclare)
            || expression.AutoAccessors?.Any(accessor => !accessor.IsStatic) == true
            || expression.Methods.Any(method => !method.IsStatic && method.IsPrivate)
            || expression.Accessors?.Any(accessor => !accessor.IsStatic && accessor.Name.Type == TokenType.PRIVATE_IDENTIFIER) == true;
        string? parentName = Expr.GetSuperclassLeafName(expression.SuperclassExpr);
        bool intrinsicArray = !_nativeAotProjectOutput && owner.BaseType == _runtime.ArrayStorage.Type;
        if ((!ownElements && !intrinsicArray) || expression.SuperclassExpr == null
            || !ContainsLexicalSuper(expression.Methods)
            || (parentName == "Array" && !intrinsicArray)
            || parentName is "Promise" or "Map" or "Set" or "WeakMap" or "WeakSet" or "Date" or "RegExp" or "Buffer"
            || Runtime.BuiltIns.BuiltInNames.IsErrorTypeName(parentName ?? "")
            || _nativeAotProjectOutput && (!SupportsReceiverInitialization(declaration)
                || expression.Accessors?.Any(accessor => accessor.Name.Type == TokenType.PRIVATE_IDENTIFIER) == true)) return;
        _lexicalSuperElementInitializers.Add(expression, owner.DefineMethod("$InitializeLexicalSuperElements",
            MethodAttributes.Assembly | MethodAttributes.Static, _types.Void,
            [_types.Object, _types.Object, _types.ObjectArray, _types.ObjectArray]));
        _usesRuntimeParents = true;
        if (_typeMap.GetClassExprType(expression) is { } checkedClass)
            _typeMapper.RegisterDynamicClassInstance(checkedClass.Core.DeclarationId);
        MarkCollectedClassExpressionReceiverArrows(expression);
    }

    private void MarkCollectedClassExpressionReceiverArrows(Expr.ClassExpr expression)
    {
        // A class returned from a function can have its arrows discovered before
        // its template is defined. Apply the same source-owner proof to those
        // already-discovered arrows before their bodies are emitted.
        var fieldArrows = new FieldInitializerArrowVisitor();
        foreach (var field in expression.Fields.Where(field => !field.IsStatic && !field.IsDeclare))
            if (field.Initializer != null) fieldArrows.Visit(field.Initializer);
        foreach (var accessor in expression.AutoAccessors?.Where(accessor => !accessor.IsStatic) ?? [])
            if (accessor.Initializer != null) fieldArrows.Visit(accessor.Initializer);
        foreach (var (arrow, captures) in _collectedArrows)
        {
            if (arrow.HasOwnThis || !captures.Contains("this")) continue;
            object? callable = arrow;
            while (callable is Expr.ArrowFunction { HasOwnThis: false } lexical)
                callable = _arrowEnclosingCallable.GetValueOrDefault(lexical);
            if (!fieldArrows.Arrows.Contains(arrow)
                && (callable is not Stmt.Function constructor
                    || !expression.Methods.Contains(constructor) || constructor.Name.Lexeme != "constructor")) continue;
            _constructorReceiverArrows.Add(arrow);
            _guestReceiverArrows.Add(arrow);
        }
    }

    private void EmitLexicalSuperElementInitializer(Expr.ClassExpr expression, TypeBuilder owner)
    {
        if (!_lexicalSuperElementInitializers.TryGetValue(expression, out var method)) return;
        var il = method.GetILGenerator();
        var context = CreateClassExpressionContext(il, expression, owner, null, method);
        foreach (var parameter in _classes.GenericParameters.Require(owner))
            context.GenericTypeParameters[parameter.Name] = parameter;
        context.HasGuestReceiver = true;
        context.GuestThisVariableName = CompilationContext.ThisParameterName;
        context.DefineParameter(CompilationContext.ThisParameterName, 0, _types.Object);
        context.ClassDefinitionParameterIndex = 1;
        context.CurrentMethodReturnType = _types.Void;
        context.ClassConstructorReceiver = il.DeclareLocal(_types.Object);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Stloc, context.ClassConstructorReceiver);
        // Reuse the constructor's already-bound cell for field-initializer
        // arrows rather than allocating a second receiver environment.
        context.ClassConstructorThisCell = il.DeclareLocal(_types.ObjectArray);
        il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Stloc, context.ClassConstructorThisCell);
        var constructor = expression.Methods.FirstOrDefault(method =>
            !method.IsStatic && method.Name.Lexeme == "constructor");
        var parameters = constructor?.Parameters.Where(parameter => parameter.IsParameterProperty).ToList() ?? [];
        var parameterFields = new HashSet<Token>(parameters.Select(parameter => parameter.Name), ReferenceEqualityComparer.Instance);
        EmitEffectiveClassElements(context, expression.Fields, null, null,
            field =>
            {
                var keys = _classExprs.Factories[expression].Keys;
                int index = Enumerable.Range(0, keys.Count).First(index => ReferenceEquals(keys[index], field.ComputedKey));
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Castclass, _runtime.ClassDefinitions.Type);
                il.Emit(OpCodes.Ldfld, _runtime.ClassDefinitions.Keys);
                il.Emit(OpCodes.Ldc_I4, index); il.Emit(OpCodes.Ldelem_Ref);
            }, expression.AutoAccessors,
            leadingFields: parameterFields);
        var emitter = new ILEmitter(context);
        foreach (var assignment in constructor?.Body?.Where(statement => IsConstructorParameterProperty(statement, constructor)) ?? [])
        {
            var value = (Expr.Variable)((Expr.Set)((Stmt.Expression)assignment).Expr).Value;
            int index = parameters.FindIndex(parameter => ReferenceEquals(parameter.Name, value.Name));
            var local = il.DeclareLocal(_types.Object);
            var storage = LexicalCellStorage.ForDeclaration(context, value.Name);
            il.Emit(OpCodes.Ldarg_3); il.Emit(OpCodes.Ldc_I4, index); il.Emit(OpCodes.Ldelem_Ref);
            il.Emit(OpCodes.Castclass, storage.ReferenceType); il.Emit(OpCodes.Ldfld, storage.ValueField);
            context.EmitLexicalTdzValueCheck(il, value.Name.Lexeme, provenCell: true);
            il.Emit(OpCodes.Stloc, local);
            emitter.EmitConstructorParameterProperty(assignment, local);
        }
        il.Emit(OpCodes.Ret);
    }

    private static bool ContainsLexicalSuper(List<Stmt.Function> methods)
    {
        var visitor = new LexicalSuperCallVisitor();
        foreach (var statement in methods.FirstOrDefault(method =>
            !method.IsStatic && method.Name.Lexeme == "constructor" && method.Body != null)?.Body ?? [])
            visitor.Visit(statement);
        return visitor.Found;
    }

    private sealed class LexicalSuperCallVisitor : AstVisitorBase
    {
        private int _arrowDepth;
        internal bool Found { get; private set; }
        protected override void VisitArrowFunction(Expr.ArrowFunction expression)
        {
            if (expression.HasOwnThis) return;
            _arrowDepth++;
            base.VisitArrowFunction(expression);
            _arrowDepth--;
        }
        protected override void VisitCall(Expr.Call expression)
        {
            if (_arrowDepth > 0 && expression.Callee is Expr.Super super
                && (super.Method == null || super.Method.Lexeme == "constructor")) Found = true;
            base.VisitCall(expression);
        }
        protected override void VisitFunction(Stmt.Function statement) { }
        protected override void VisitClass(Stmt.Class statement) { }
        protected override void VisitClassExpr(Expr.ClassExpr expression) { }
    }

    private sealed class FieldInitializerArrowVisitor : AstVisitorBase
    {
        internal HashSet<Expr.ArrowFunction> Arrows { get; } = new(ReferenceEqualityComparer.Instance);
        protected override void VisitArrowFunction(Expr.ArrowFunction expression)
        {
            if (expression.HasOwnThis) return;
            Arrows.Add(expression);
            base.VisitArrowFunction(expression);
        }
        protected override void VisitFunction(Stmt.Function statement) { }
        protected override void VisitClass(Stmt.Class statement) { }
        protected override void VisitClassExpr(Expr.ClassExpr expression) { }
    }
}
