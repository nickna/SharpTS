using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Proves shared numeric display-class fields independently of iterator protocol
/// stability. These are live function-owned cells, not per-iteration snapshots.
/// </summary>
internal static class StableNumericFunctionCaptureAnalyzer
{
    internal static void MarkCaptures(
        Expr.ArrowFunction next,
        object? owner,
        ClosureAnalyzer closures,
        TypeMap typeMap)
    {
        if (owner is not Stmt.Function source || source.Body is null || source.IsAsync || source.IsGenerator ||
            next.IsAsync || next.IsGenerator)
            return;

        int iteratorDeclaration = source.Body.FindIndex(statement => statement switch
        {
            Stmt.Var { Initializer: Expr.ObjectLiteral literal } =>
                literal.Properties.Any(property => ReferenceEquals(property.Value, next)),
            Stmt.Const { Initializer: Expr.ObjectLiteral literal } =>
                literal.Properties.Any(property => ReferenceEquals(property.Value, next)),
            _ => false
        });
        if (iteratorDeclaration < 0)
            return;

        foreach (string name in closures.GetCaptures(next))
        {
            if (!ReferenceEquals(closures.GetCaptureSource(next, name), source) ||
                IsCapturedByAnotherCallable(source.Body, next, name, closures) ||
                StableNumericFunctionCaptureAnalyzer.HasAmbiguousBinding(source, name) ||
                !HasStableNumericBinding(source, name, iteratorDeclaration, typeMap))
                continue;

            typeMap.MarkStableNumericFunctionCaptureField(source, name);
        }
    }

    private static bool HasStableNumericBinding(
        Stmt.Function source,
        string name,
        int iteratorDeclaration,
        TypeMap typeMap)
    {
        bool initialized = source.Parameters.Any(parameter =>
            parameter.Name.Lexeme == name && parameter.Type == "number" &&
            !typeMap.IsUndefinedReachableNumericParam(parameter));

        if (!initialized)
        {
            for (int index = 0; index < iteratorDeclaration; index++)
            {
                initialized = source.Body![index] switch
                {
                    Stmt.Var declaration when declaration.Name.Lexeme == name &&
                        declaration.TypeAnnotation == "number" &&
                        declaration.Initializer is not null &&
                        !typeMap.IsUndefinedReachableNumericLocal(declaration) &&
                        !typeMap.IsUndefinedReachableNumericLocal(declaration.Initializer) &&
                        IsNumber(typeMap.Get(declaration.Initializer)) => true,
                    Stmt.Const declaration when declaration.Name.Lexeme == name &&
                        declaration.TypeAnnotation == "number" &&
                        !typeMap.IsUndefinedReachableNumericLocal(declaration.Initializer) &&
                        IsNumber(typeMap.Get(declaration.Initializer)) => true,
                    _ => initialized
                };
            }
        }

        if (!initialized)
            return false;

        var writes = new NumericWriteVisitor(name, typeMap);
        foreach (var statement in source.Body!)
            writes.Visit(statement);
        return writes.Valid;
    }

    private static bool IsCapturedByAnotherCallable(
        IEnumerable<Stmt> body,
        Expr.ArrowFunction next,
        string name,
        ClosureAnalyzer closures)
    {
        var visitor = new OtherCaptureVisitor(next, name, closures);
        foreach (var statement in body)
            visitor.Visit(statement);
        return visitor.Found;
    }

    internal static bool IsNumber(TypeInfo? type) => type is
        TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or TypeInfo.NumberLiteral;

    internal sealed class NumericWriteVisitor(
        string name,
        TypeMap typeMap,
        Expr.Assign? allowedUnknownAssignment = null) : AstVisitorBase
    {
        public bool Valid { get; private set; } = true;

        protected override void VisitAssign(Expr.Assign expression)
        {
            if (expression.Name.Lexeme == name &&
                !ReferenceEquals(expression, allowedUnknownAssignment) &&
                !IsNumber(typeMap.Get(expression.Value)))
                Valid = false;
            base.VisitAssign(expression);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expression)
        {
            if (expression.Name.Lexeme == name &&
                (expression.Operator.Type is not (TokenType.PLUS_EQUAL or TokenType.MINUS_EQUAL or
                    TokenType.STAR_EQUAL or TokenType.SLASH_EQUAL or TokenType.PERCENT_EQUAL) ||
                 !IsNumber(typeMap.Get(expression.Value))))
                Valid = false;
            base.VisitCompoundAssign(expression);
        }

        protected override void VisitLogicalAssign(Expr.LogicalAssign expression)
        {
            if (expression.Name.Lexeme == name)
                Valid = false;
            base.VisitLogicalAssign(expression);
        }

        protected override void VisitForOf(Stmt.ForOf statement)
        {
            if (statement.Variable.Lexeme == name)
                Valid = false;
            base.VisitForOf(statement);
        }

        protected override void VisitForIn(Stmt.ForIn statement)
        {
            if (statement.Variable.Lexeme == name)
                Valid = false;
            base.VisitForIn(statement);
        }
    }

    private sealed class OtherCaptureVisitor(
        Expr.ArrowFunction next, string name, ClosureAnalyzer closures) : AstVisitorBase
    {
        public bool Found { get; private set; }

        protected override void VisitFunction(Stmt.Function statement)
        {
            if (closures.GetCaptures(statement).Contains(name))
                Found = true;
            base.VisitFunction(statement);
        }

        protected override void VisitArrowFunction(Expr.ArrowFunction expression)
        {
            if (!ReferenceEquals(expression, next) &&
                closures.GetCaptures(expression).Contains(name))
                Found = true;
            base.VisitArrowFunction(expression);
        }
    }

    public static void Analyze(List<Stmt> program, TypeMap? typeMap, ClosureAnalyzer? closures)
    {
        if (typeMap is null || closures is null) return;
        var visitor = new FunctionVisitor();
        foreach (var statement in program) visitor.Visit(statement);
        if (visitor.HasEval) return;
        foreach (var function in visitor.Functions)
        {
            if (function.Body is null || function.IsAsync || function.IsGenerator) continue;
            foreach (var statement in function.Body)
            {
                Expr.ObjectLiteral? literal = statement switch
                {
                    Stmt.Var { Initializer: Expr.ObjectLiteral value } => value,
                    Stmt.Const { Initializer: Expr.ObjectLiteral value } => value,
                    _ => null
                };
                if (literal is null) continue;
                foreach (var property in literal.Properties)
                    if (property.Value is Expr.ArrowFunction method)
                        MarkCaptures(
                            method, function, closures, typeMap);
            }
        }
    }

    internal static bool HasAmbiguousBinding(Stmt.Function source, string name)
    {
        var visitor = new BindingVisitor(name);
        visitor.Count = source.Parameters.Count(parameter => parameter.Name.Lexeme == name);
        foreach (var statement in source.Body!) visitor.Visit(statement);
        return visitor.Count != 1;
    }

    private sealed class FunctionVisitor : AstVisitorBase
    {
        public List<Stmt.Function> Functions { get; } = [];
        public bool HasEval { get; private set; }
        protected override void VisitFunction(Stmt.Function statement)
        {
            Functions.Add(statement);
            base.VisitFunction(statement);
        }
        protected override void VisitCall(Expr.Call expression)
        {
            if (expression.Callee is Expr.Variable { Name.Lexeme: "eval" }) HasEval = true;
            base.VisitCall(expression);
        }
    }

    private sealed class BindingVisitor(string name) : AstVisitorBase
    {
        public int Count { get; set; }
        protected override void VisitVar(Stmt.Var statement)
        {
            if (statement.Name.Lexeme == name) Count++;
            base.VisitVar(statement);
        }
        protected override void VisitConst(Stmt.Const statement)
        {
            if (statement.Name.Lexeme == name) Count++;
            base.VisitConst(statement);
        }
        protected override void VisitArrowFunction(Expr.ArrowFunction expression)
        {
            Count += expression.Parameters.Count(parameter => parameter.Name.Lexeme == name);
            base.VisitArrowFunction(expression);
        }
        protected override void VisitFunction(Stmt.Function statement)
        {
            if (statement.Name.Lexeme == name) Count++;
            Count += statement.Parameters.Count(parameter => parameter.Name.Lexeme == name);
            base.VisitFunction(statement);
        }
    }
}
