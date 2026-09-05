using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Finds stable synchronous functions whose numeric rest array can be represented by a
/// fixed set of native <see cref="double"/> parameters at proven call sites.
/// The ordinary List&lt;object&gt; ABI remains available for unknown or dynamic calls.
/// </summary>
internal static class StableNumericRestFunctionAnalyzer
{
    internal const int MaximumVariantsPerFunction = 8;
    internal const int MaximumVariantsPerCompilation = 64;
    internal const int MaximumRestArity = 32;

    internal sealed record Variant(int RestArity,
        IReadOnlyDictionary<Expr.GetIndex, int> Reads, List<Expr.Call> Calls)
    {
        public bool HasLiteralIndices => Reads.Keys.All(read =>
            TryEvaluateIndex(read.Index, EmptyConstants, out _));
    }

    private static readonly IReadOnlyDictionary<string, double> EmptyConstants = new Dictionary<string, double>();

    internal sealed record Info(string RestName, int RegularParameterCount,
        IReadOnlyList<Variant> Variants);

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

        var callsByTarget = NumericRestCallBindingAnalyzer.Analyze(statements, functions, stableFunctions);
        int remainingVariants = MaximumVariantsPerCompilation - results.Values.Sum(info => info.Variants.Count);

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

            int regularCount = function.Parameters.Count - 1;
            var variants = new List<Variant>();
            // Preserve existing direct-call specialization in closures/methods.
            // These arity-only companions never substitute regular parameters;
            // the emitter's ordinary lexical/receiver guards still select them.
            var literalUsage = new RestUsageAnalyzer(rest.Name.Lexeme, EmptyConstants);
            foreach (var statement in function.Body) literalUsage.Visit(statement);
            if (literalUsage.IsEligible)
            {
                var directCalls = new LiteralArityCollector(function.Name.Lexeme, regularCount,
                    literalUsage.MaximumReadIndex, typeMap);
                foreach (var statement in statements) directCalls.Visit(statement);
                foreach (int arity in directCalls.Arities.Order())
                {
                    if (variants.Count >= MaximumVariantsPerFunction || remainingVariants <= 0) break;
                    variants.Add(new Variant(arity, literalUsage.Reads, []));
                    remainingVariants--;
                }
            }
            var calls = callsByTarget.GetValueOrDefault(function) ?? [];
            foreach (var call in calls)
            {
                int arity = call.Arguments.Count - regularCount;
                if (call.Optional || arity < 0 || arity > MaximumRestArity
                    || call.Arguments.Any(arg => arg is Expr.Spread)
                    || !call.Arguments.All(arg => IsNumeric(typeMap.Get(arg))))
                    continue;

                var constants = new Dictionary<string, double>(StringComparer.Ordinal);
                for (int i = 0; i < regularCount; i++)
                    if (TryEvaluateIndex(call.Arguments[i], EmptyConstants, out double value))
                        constants[function.Parameters[i].Name.Lexeme] = value;

                var usage = new RestUsageAnalyzer(rest.Name.Lexeme, constants);
                foreach (var statement in function.Body) usage.Visit(statement);
                if (!usage.IsEligible || usage.MaximumReadIndex >= arity)
                    continue;

                // The emitted body substitutes only these proven index reads.
                // All regular arguments remain real arguments: no shared AST is
                // cloned or rewritten, and arithmetic elsewhere is untouched.
                var existing = variants.FirstOrDefault(v => v.RestArity == arity
                    && v.Reads.Count == usage.Reads.Count
                    && v.Reads.All(pair => usage.Reads.TryGetValue(pair.Key, out int index) && index == pair.Value));
                if (existing != null)
                    existing.Calls.Add(call);
                else if (variants.Count < MaximumVariantsPerFunction && remainingVariants > 0)
                {
                    variants.Add(new Variant(arity, usage.Reads, new List<Expr.Call> { call }));
                    remainingVariants--;
                }
            }
            if (variants.Count > 0)
                results[function] = new Info(rest.Name.Lexeme, regularCount, variants);
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

    private sealed class RestUsageAnalyzer(string restName, IReadOnlyDictionary<string, double> constants) : AstVisitorBase
    {
        public bool IsEligible { get; private set; } = true;
        public int MaximumReadIndex { get; private set; } = -1;
        public Dictionary<Expr.GetIndex, int> Reads { get; } = new(ReferenceEqualityComparer.Instance);

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
            if (statement.Name.Lexeme == restName || constants.ContainsKey(statement.Name.Lexeme))
            {
                IsEligible = false;
                ShouldContinue = false;
                return;
            }
            base.VisitVar(statement);
        }

        protected override void VisitConst(Stmt.Const statement)
        {
            if (statement.Name.Lexeme == restName || constants.ContainsKey(statement.Name.Lexeme))
            {
                IsEligible = false;
                ShouldContinue = false;
                return;
            }
            base.VisitConst(statement);
        }

        protected override void VisitAssign(Expr.Assign expression)
        {
            if (expression.Name.Lexeme == restName || constants.ContainsKey(expression.Name.Lexeme))
            {
                Reject();
                return;
            }
            base.VisitAssign(expression);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expression)
        {
            if (expression.Name.Lexeme == restName || constants.ContainsKey(expression.Name.Lexeme))
            {
                Reject();
                return;
            }
            base.VisitCompoundAssign(expression);
        }

        protected override void VisitLogicalAssign(Expr.LogicalAssign expression)
        {
            if (expression.Name.Lexeme == restName || constants.ContainsKey(expression.Name.Lexeme))
            {
                Reject();
                return;
            }
            base.VisitLogicalAssign(expression);
        }

        protected override void VisitDelete(Expr.Delete expression)
        {
            if (IsRestMember(expression.Operand) || IsConstantVariable(expression.Operand))
            {
                Reject();
                return;
            }
            base.VisitDelete(expression);
        }

        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expression)
        {
            if (IsRestMember(expression.Operand) || IsConstantVariable(expression.Operand))
            {
                Reject();
                return;
            }
            base.VisitPrefixIncrement(expression);
        }

        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expression)
        {
            if (IsRestMember(expression.Operand) || IsConstantVariable(expression.Operand))
            {
                Reject();
                return;
            }
            base.VisitPostfixIncrement(expression);
        }

        protected override void VisitForOf(Stmt.ForOf statement)
        {
            if (statement.Variable.Lexeme == restName || constants.ContainsKey(statement.Variable.Lexeme))
            {
                Reject();
                return;
            }
            base.VisitForOf(statement);
        }

        protected override void VisitForIn(Stmt.ForIn statement)
        {
            if (statement.Variable.Lexeme == restName || constants.ContainsKey(statement.Variable.Lexeme))
            {
                Reject();
                return;
            }
            base.VisitForIn(statement);
        }

        protected override void VisitTryCatch(Stmt.TryCatch statement)
        {
            if (statement.CatchParam?.Lexeme == restName
                || statement.CatchParam != null && constants.ContainsKey(statement.CatchParam.Lexeme))
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
                    || !TryEvaluateIndex(expression.Index, constants, out double index)
                    || index < 0
                    || index != Math.Truncate(index)
                    || index > int.MaxValue)
                {
                    IsEligible = false;
                    ShouldContinue = false;
                    return;
                }

                MaximumReadIndex = Math.Max(MaximumReadIndex, (int)index);
                Reads[expression] = (int)index;
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

        private bool IsConstantVariable(Expr expression) => expression switch
        {
            Expr.Variable variable => constants.ContainsKey(variable.Name.Lexeme),
            Expr.Grouping grouping => IsConstantVariable(grouping.Expression),
            _ => false
        };

        protected override void VisitClass(Stmt.Class statement) => Reject();
        protected override void VisitClassExpr(Expr.ClassExpr expression) => Reject();
        protected override void VisitThis(Expr.This expression) => Reject();
        protected override void VisitSuper(Expr.Super expression) => Reject();

        protected override void VisitDestructuringAssign(Expr.DestructuringAssign expression) => Reject();

        protected override void VisitCall(Expr.Call expression)
        {
            if (expression.Callee is Expr.Variable { Name.Lexeme: "eval" }) Reject();
            else base.VisitCall(expression);
        }

        private void Reject()
        {
            IsEligible = false;
            ShouldContinue = false;
        }
    }

    private static bool TryEvaluateIndex(Expr expression, IReadOnlyDictionary<string, double> constants,
        out double value)
    {
        switch (expression)
        {
            case Expr.Literal { Value: double number }:
                value = number;
                return double.IsFinite(value);
            case Expr.Variable variable:
                return constants.TryGetValue(variable.Name.Lexeme, out value);
            case Expr.Grouping grouping:
                return TryEvaluateIndex(grouping.Expression, constants, out value);
            case Expr.Unary { Operator.Type: TokenType.MINUS } unary
                when TryEvaluateIndex(unary.Right, constants, out double operand):
                value = -operand;
                return true;
            case Expr.Binary binary when binary.Operator.Type is TokenType.PLUS or TokenType.MINUS
                && TryEvaluateIndex(binary.Left, constants, out double left)
                && TryEvaluateIndex(binary.Right, constants, out double right):
                value = binary.Operator.Type == TokenType.PLUS ? left + right : left - right;
                return double.IsFinite(value);
            default:
                value = 0;
                return false;
        }
    }

    private sealed class LiteralArityCollector(string functionName, int regularCount,
        int maximumIndex, TypeMap types) : AstVisitorBase
    {
        public HashSet<int> Arities { get; } = [];
        protected override void VisitCall(Expr.Call call)
        {
            int arity = call.Arguments.Count - regularCount;
            if (!call.Optional && call.Callee is Expr.Variable variable
                && variable.Name.Lexeme == functionName && arity > maximumIndex
                && arity >= 0 && arity <= MaximumRestArity
                && call.Arguments.All(arg => arg is not Expr.Spread)
                && call.Arguments.Skip(regularCount).All(arg => IsNumeric(types.Get(arg))))
                Arities.Add(arity);
            base.VisitCall(call);
        }
    }

    private static bool IsNumeric(TypeInfo? type) => type is
        TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER }
        or TypeInfo.NumberLiteral;
}
