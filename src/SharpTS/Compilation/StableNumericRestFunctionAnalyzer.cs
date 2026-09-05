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
        IReadOnlySet<int> RestArities,
        IReadOnlyList<Expr.Call> AliasCalls,
        IReadOnlyList<Specialization> Specializations);

    internal sealed record Specialization(
        int RestArity,
        IReadOnlyDictionary<Expr.GetIndex, int> Indices,
        List<Expr.Call> Calls);

    public static void Analyze(
        IReadOnlyList<Stmt> statements,
        TypeMap typeMap,
        IReadOnlySet<Stmt.Function> stableFunctions,
        ClosureAnalyzer closureAnalyzer,
        IDictionary<Stmt.Function, Info> results)
    {
        var aliases = StableNumericRestAliasAnalyzer.Analyze(statements, stableFunctions);
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
            {
                var specializations = AnalyzeConstantCalls(function, rest.Name.Lexeme, aliases, typeMap);
                if (specializations.Count != 0)
                    results[function] = new Info(rest.Name.Lexeme, function.Parameters.Count - 1,
                        -1, new HashSet<int>(), [], specializations);
                continue;
            }

            int regularCount = function.Parameters.Count - 1;
            var calls = new FixedArityCallAnalyzer(
                function.Name.Lexeme,
                regularCount,
                usage.MaximumReadIndex,
                typeMap, aliases, function);
            foreach (var statement in statements)
                calls.Visit(statement);
            if (calls.RestArities.Count == 0)
                continue;

            results[function] = new Info(
                rest.Name.Lexeme,
                regularCount,
                usage.MaximumReadIndex,
                calls.RestArities, calls.AliasCalls, []);
        }
    }

    private static List<Specialization> AnalyzeConstantCalls(
        Stmt.Function function, string restName,
        IReadOnlyDictionary<Expr.Call, Stmt.Function> provenCalls, TypeMap typeMap)
    {
        // Bound code growth independently of how many call sites the module contains.
        const int maximumVariants = 8;
        int regularCount = function.Parameters.Count - 1;
        var variants = new Dictionary<string, Specialization>(StringComparer.Ordinal);
        if (regularCount == 0) return [];
        foreach (var (call, target) in provenCalls)
        {
            if (!ReferenceEquals(target, function) || call.Arguments.Count <= regularCount
                || call.Arguments.Any(a => a is Expr.Spread)
                || !call.Arguments.Skip(regularCount).All(a => IsNumeric(typeMap.Get(a))))
                continue;
            var constants = new Dictionary<string, double>(StringComparer.Ordinal);
            for (int i = 0; i < regularCount; i++)
            {
                if (call.Arguments[i] is not Expr.Literal { Value: double value } || !double.IsFinite(value))
                    break;
                constants[function.Parameters[i].Name.Lexeme] = value;
            }
            if (constants.Count != regularCount) continue;
            int arity = call.Arguments.Count - regularCount;
            string key = arity + ":" + string.Join(",", constants.Values.Select(BitConverter.DoubleToInt64Bits));
            if (variants.TryGetValue(key, out var existing))
            {
                existing.Calls.Add(call);
                continue;
            }
            if (variants.Count >= maximumVariants) continue;
            var usage = new RestUsageAnalyzer(restName, constants);
            foreach (var statement in function.Body!) usage.Visit(statement);
            if (!usage.IsEligible || usage.MaximumReadIndex >= arity) continue;
            variants[key] = new Specialization(arity, usage.Indices, [call]);
        }
        return variants.Values.ToList();
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

    private sealed class RestUsageAnalyzer(
        string restName, IReadOnlyDictionary<string, double>? constants = null) : AstVisitorBase
    {
        public bool IsEligible { get; private set; } = true;
        public int MaximumReadIndex { get; private set; } = -1;
        public Dictionary<Expr.GetIndex, int> Indices { get; } = new(ReferenceEqualityComparer.Instance);
        private bool IsProtectedName(string name) => name == restName || constants?.ContainsKey(name) == true;

        private bool TryIndex(Expr expression, out double value)
        {
            // Ordinary companions only rewrite literal indices. Expression-index
            // rewrites belong to the per-call specialization's identity-keyed map.
            if (constants == null && expression is not Expr.Literal)
            {
                value = 0;
                return false;
            }
            switch (expression)
            {
                case Expr.Literal { Value: double literal }: value = literal; return true;
                case Expr.Variable variable when constants != null:
                    return constants.TryGetValue(variable.Name.Lexeme, out value);
                case Expr.Grouping grouping: return TryIndex(grouping.Expression, out value);
                case Expr.Binary binary when binary.Operator.Type is TokenType.PLUS or TokenType.MINUS:
                    if (TryIndex(binary.Left, out double left) && TryIndex(binary.Right, out double right))
                    {
                        value = binary.Operator.Type == TokenType.PLUS ? left + right : left - right;
                        return true;
                    }
                    break;
            }
            value = 0;
            return false;
        }

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
            if (IsProtectedName(statement.Name.Lexeme))
            {
                IsEligible = false;
                ShouldContinue = false;
                return;
            }
            base.VisitVar(statement);
        }

        protected override void VisitConst(Stmt.Const statement)
        {
            if (IsProtectedName(statement.Name.Lexeme))
            {
                IsEligible = false;
                ShouldContinue = false;
                return;
            }
            base.VisitConst(statement);
        }

        protected override void VisitAssign(Expr.Assign expression)
        {
            if (IsProtectedName(expression.Name.Lexeme))
            {
                Reject();
                return;
            }
            base.VisitAssign(expression);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expression)
        {
            if (IsProtectedName(expression.Name.Lexeme))
            {
                Reject();
                return;
            }
            base.VisitCompoundAssign(expression);
        }

        protected override void VisitLogicalAssign(Expr.LogicalAssign expression)
        {
            if (IsProtectedName(expression.Name.Lexeme))
            {
                Reject();
                return;
            }
            base.VisitLogicalAssign(expression);
        }

        protected override void VisitDestructuringAssign(Expr.DestructuringAssign expression)
        {
            // Destructuring targets are patterns, not ordinary variable visits.
            // Retain the ordinary ABI rather than miss a write to an index parameter.
            Reject();
        }

        protected override void VisitUsing(Stmt.Using statement) => Reject();

        protected override void VisitDelete(Expr.Delete expression)
        {
            if (IsRestMember(expression.Operand)
                || (expression.Operand is Expr.Variable variable && IsProtectedName(variable.Name.Lexeme)))
            {
                Reject();
                return;
            }
            base.VisitDelete(expression);
        }

        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expression)
        {
            if (IsRestMember(expression.Operand)
                || (expression.Operand is Expr.Variable variable && IsProtectedName(variable.Name.Lexeme)))
            {
                Reject();
                return;
            }
            base.VisitPrefixIncrement(expression);
        }

        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expression)
        {
            if (IsRestMember(expression.Operand)
                || (expression.Operand is Expr.Variable variable && IsProtectedName(variable.Name.Lexeme)))
            {
                Reject();
                return;
            }
            base.VisitPostfixIncrement(expression);
        }

        protected override void VisitForOf(Stmt.ForOf statement)
        {
            if (IsProtectedName(statement.Variable.Lexeme))
            {
                Reject();
                return;
            }
            base.VisitForOf(statement);
        }

        protected override void VisitForIn(Stmt.ForIn statement)
        {
            if (IsProtectedName(statement.Variable.Lexeme))
            {
                Reject();
                return;
            }
            base.VisitForIn(statement);
        }

        protected override void VisitTryCatch(Stmt.TryCatch statement)
        {
            if (statement.CatchParam != null && IsProtectedName(statement.CatchParam.Lexeme))
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
                    || !TryIndex(expression.Index, out double index)
                    || !double.IsFinite(index)
                    || index < 0
                    || index != Math.Truncate(index)
                    || index > int.MaxValue)
                {
                    IsEligible = false;
                    ShouldContinue = false;
                    return;
                }

                MaximumReadIndex = Math.Max(MaximumReadIndex, (int)index);
                Indices[expression] = (int)index;
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
        TypeMap typeMap,
        IReadOnlyDictionary<Expr.Call, Stmt.Function> aliases,
        Stmt.Function target) : AstVisitorBase
    {
        public HashSet<int> RestArities { get; } = [];
        public List<Expr.Call> AliasCalls { get; } = [];

        protected override void VisitCall(Expr.Call expression)
        {
            if (!expression.Optional
                && expression.Callee is Expr.Variable variable
                && (variable.Name.Lexeme == functionName
                    || (aliases.TryGetValue(expression, out var aliasTarget) && ReferenceEquals(aliasTarget, target)))
                && expression.Arguments.Count >= regularCount
                && expression.Arguments.All(argument => argument is not Expr.Spread)
                && expression.Arguments.Skip(regularCount).All(
                    argument => IsNumeric(typeMap.Get(argument))))
            {
                int restArity = expression.Arguments.Count - regularCount;
                if (restArity > maximumReadIndex)
                {
                    RestArities.Add(restArity);
                    if (aliases.ContainsKey(expression) && variable.Name.Lexeme != functionName) AliasCalls.Add(expression);
                }
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
