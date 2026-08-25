using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Finds stable synchronous functions whose numeric rest array can be represented by a
/// fixed set of native <see cref="double"/> parameters at statically direct call sites.
/// The ordinary List&lt;object&gt; ABI remains available for every indirect or dynamic call.
/// </summary>
internal static class StableNumericRestFunctionAnalyzer
{
    internal sealed record Info(
        string RestName,
        int RegularParameterCount,
        int MaximumReadIndex,
        IReadOnlySet<int> RestArities);

    public static void Analyze(
        IReadOnlyList<Stmt> statements,
        TypeMap typeMap,
        IReadOnlySet<Stmt.Function> stableFunctions,
        ClosureAnalyzer closureAnalyzer,
        IDictionary<Stmt.Function, Info> results)
    {
        var functions = new List<Stmt.Function>();
        foreach (var statement in statements)
            CollectTopLevelFunctions(statement, functions);

        foreach (var function in functions)
        {
            if (!stableFunctions.Contains(function)
                || function.Body == null
                || function.IsAsync
                || function.IsGenerator
                || function.TypeParams is { Count: > 0 }
                || function.Parameters.Count == 0
                || function.Parameters[^1] is not { IsRest: true } rest
                || function.Parameters.Take(function.Parameters.Count - 1)
                    .Any(parameter => parameter.DefaultValue != null || parameter.IsOptional)
                || closureAnalyzer.GetCapturedLocals(function).Count != 0
                || !HasNumericRestType(function, typeMap))
            {
                continue;
            }

            var usage = new RestUsageAnalyzer(rest.Name.Lexeme);
            foreach (var statement in function.Body)
                usage.Visit(statement);
            if (!usage.IsEligible)
                continue;

            int regularCount = function.Parameters.Count - 1;
            var calls = new FixedArityCallAnalyzer(
                function.Name.Lexeme,
                regularCount,
                usage.MaximumReadIndex,
                typeMap);
            foreach (var statement in statements)
                calls.Visit(statement);
            if (calls.RestArities.Count == 0)
                continue;

            results[function] = new Info(
                rest.Name.Lexeme,
                regularCount,
                usage.MaximumReadIndex,
                calls.RestArities);
        }
    }

    private static bool HasNumericRestType(Stmt.Function function, TypeMap typeMap)
    {
        var functionType = typeMap.GetFunctionType(function.Name.Lexeme);
        if (functionType == null || functionType.ParamTypes.Count != function.Parameters.Count)
            return false;

        return functionType.ParamTypes
                .Take(functionType.ParamTypes.Count - 1)
                .All(IsNumeric)
            && functionType.ParamTypes[^1] is TypeInfo.Array
            {
                ElementType: TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER }
            };
    }

    private static void CollectTopLevelFunctions(Stmt statement, ICollection<Stmt.Function> functions)
    {
        switch (statement)
        {
            case Stmt.Function function:
                functions.Add(function);
                break;
            case Stmt.Export { Declaration: { } declaration }:
                CollectTopLevelFunctions(declaration, functions);
                break;
            case Stmt.Sequence sequence:
                foreach (var inner in sequence.Statements)
                    CollectTopLevelFunctions(inner, functions);
                break;
        }
    }

    private sealed class RestUsageAnalyzer(string restName) : AstVisitorBase
    {
        public bool IsEligible { get; private set; } = true;
        public int MaximumReadIndex { get; private set; } = -1;

        protected override void VisitVariable(Expr.Variable expression)
        {
            if (expression.Name.Lexeme == restName
                || expression.Name.Lexeme == "arguments")
            {
                IsEligible = false;
                ShouldContinue = false;
            }
        }

        protected override void VisitVar(Stmt.Var statement)
        {
            if (statement.Name.Lexeme == restName)
            {
                IsEligible = false;
                ShouldContinue = false;
                return;
            }
            base.VisitVar(statement);
        }

        protected override void VisitConst(Stmt.Const statement)
        {
            if (statement.Name.Lexeme == restName)
            {
                IsEligible = false;
                ShouldContinue = false;
                return;
            }
            base.VisitConst(statement);
        }

        protected override void VisitAssign(Expr.Assign expression)
        {
            if (expression.Name.Lexeme == restName)
            {
                Reject();
                return;
            }
            base.VisitAssign(expression);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expression)
        {
            if (expression.Name.Lexeme == restName)
            {
                Reject();
                return;
            }
            base.VisitCompoundAssign(expression);
        }

        protected override void VisitLogicalAssign(Expr.LogicalAssign expression)
        {
            if (expression.Name.Lexeme == restName)
            {
                Reject();
                return;
            }
            base.VisitLogicalAssign(expression);
        }

        protected override void VisitDelete(Expr.Delete expression)
        {
            if (IsRestMember(expression.Operand))
            {
                Reject();
                return;
            }
            base.VisitDelete(expression);
        }

        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expression)
        {
            if (IsRestMember(expression.Operand))
            {
                Reject();
                return;
            }
            base.VisitPrefixIncrement(expression);
        }

        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expression)
        {
            if (IsRestMember(expression.Operand))
            {
                Reject();
                return;
            }
            base.VisitPostfixIncrement(expression);
        }

        protected override void VisitForOf(Stmt.ForOf statement)
        {
            if (statement.Variable.Lexeme == restName)
            {
                Reject();
                return;
            }
            base.VisitForOf(statement);
        }

        protected override void VisitForIn(Stmt.ForIn statement)
        {
            if (statement.Variable.Lexeme == restName)
            {
                Reject();
                return;
            }
            base.VisitForIn(statement);
        }

        protected override void VisitTryCatch(Stmt.TryCatch statement)
        {
            if (statement.CatchParam?.Lexeme == restName)
            {
                Reject();
                return;
            }
            base.VisitTryCatch(statement);
        }

        protected override void VisitGetIndex(Expr.GetIndex expression)
        {
            if (expression.Object is Expr.Variable variable && variable.Name.Lexeme == restName)
            {
                if (expression.Optional
                    || expression.Index is not Expr.Literal { Value: double index }
                    || index < 0
                    || index != Math.Truncate(index)
                    || index > int.MaxValue)
                {
                    IsEligible = false;
                    ShouldContinue = false;
                    return;
                }

                MaximumReadIndex = Math.Max(MaximumReadIndex, (int)index);
                return;
            }

            base.VisitGetIndex(expression);
        }

        protected override void VisitGet(Expr.Get expression)
        {
            if (expression.Object is Expr.Variable variable && variable.Name.Lexeme == restName)
            {
                if (!expression.Optional && expression.Name.Lexeme == "length")
                    return;

                IsEligible = false;
                ShouldContinue = false;
                return;
            }

            base.VisitGet(expression);
        }

        // The specialized body deliberately has no closure/display-class setup.
        // Retain the ordinary ABI for bodies containing nested callable scopes.
        protected override void VisitFunction(Stmt.Function statement)
        {
            IsEligible = false;
            ShouldContinue = false;
        }

        protected override void VisitArrowFunction(Expr.ArrowFunction expression)
        {
            Reject();
        }

        private bool IsRestMember(Expr expression) => expression switch
        {
            Expr.Get { Object: Expr.Variable variable } => variable.Name.Lexeme == restName,
            Expr.GetIndex { Object: Expr.Variable variable } => variable.Name.Lexeme == restName,
            _ => false
        };

        private void Reject()
        {
            IsEligible = false;
            ShouldContinue = false;
        }
    }

    private sealed class FixedArityCallAnalyzer(
        string functionName,
        int regularCount,
        int maximumReadIndex,
        TypeMap typeMap) : AstVisitorBase
    {
        public HashSet<int> RestArities { get; } = [];

        protected override void VisitCall(Expr.Call expression)
        {
            if (!expression.Optional
                && expression.Callee is Expr.Variable variable
                && variable.Name.Lexeme == functionName
                && expression.Arguments.Count >= regularCount
                && expression.Arguments.All(argument => argument is not Expr.Spread)
                && expression.Arguments.Skip(regularCount).All(
                    argument => IsNumeric(typeMap.Get(argument))))
            {
                int restArity = expression.Arguments.Count - regularCount;
                if (restArity > maximumReadIndex)
                    RestArities.Add(restArity);
            }

            base.VisitCall(expression);
        }

        private static bool IsNumeric(TypeInfo? type) => type is
            TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER }
            or TypeInfo.NumberLiteral;
    }

    private static bool IsNumeric(TypeInfo? type) => type is
        TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER }
        or TypeInfo.NumberLiteral;
}
