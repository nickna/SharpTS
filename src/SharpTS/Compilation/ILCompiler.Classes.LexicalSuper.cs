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
        var lexicalSuper = new LexicalSuperCallVisitor();
        foreach (var statement in expression.Methods.FirstOrDefault(method =>
            !method.IsStatic && method.Name.Lexeme == "constructor")?.Body ?? []) lexicalSuper.Visit(statement);
        if (!expression.Fields.Any(field => !field.IsStatic && !field.IsDeclare)
            || !lexicalSuper.Found
            || !SupportsReceiverInitialization(declaration)
            || (!HasRuntimeParent(expression) && !RequiresConstructorReturnAdapter(declaration))
            || expression.Methods.Any(method => method.Name.Lexeme == "constructor"
                && method.Parameters.Any(parameter => parameter.IsParameterProperty))) return;
        _lexicalSuperElementInitializers.Add(expression, owner.DefineMethod("$InitializeLexicalSuperElements",
            MethodAttributes.Assembly | MethodAttributes.Static, _types.Void, [_types.Object, _types.Object, _types.ObjectArray]));
        // A class returned from a function can have its arrows discovered before
        // its template is defined. Apply the same source-owner proof to those
        // already-discovered arrows before their display classes are emitted.
        var fieldArrows = new FieldInitializerArrowVisitor();
        foreach (var field in expression.Fields.Where(field => !field.IsStatic && !field.IsDeclare))
            if (field.Initializer != null) fieldArrows.Visit(field.Initializer);
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
        EmitEffectiveClassElements(context, expression.Fields, null, null,
            _ => throw new InvalidOperationException("Lexical super elements require ordinary field keys."));
        il.Emit(OpCodes.Ret);
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
