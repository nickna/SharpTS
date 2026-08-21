using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Proves which module-private, call-only functions may expose generated compact-record
/// carrier types in their CLR signatures. The proof is intentionally whole-module and
/// conservative: any value use or export retains the ordinary JavaScript object ABI.
/// </summary>
internal static class ExactCompactRecordFunctionAnalyzer
{
    internal static void Analyze(
        IReadOnlyList<Stmt> statements,
        TypeMap? typeMap,
        RuntimeFeatureSet? features,
        IReadOnlySet<Stmt.Function> stableFunctions,
        Dictionary<Stmt.Function, HashSet<int>> exactParameters,
        Dictionary<Stmt.Function, string> exactReturns)
    {
        if (typeMap is null || features is null)
            return;

        var topLevelFunctions = new List<Stmt.Function>();
        var exportedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var statement in statements)
            CollectTopLevel(statement, topLevelFunctions, exportedNames, exported: false);

        foreach (var statement in statements)
        {
            if (statement is Stmt.Export { NamedExports: { } exports, FromModulePath: null })
            {
                foreach (var export in exports)
                    if (!export.IsTypeOnly)
                        exportedNames.Add(export.LocalName.Lexeme);
            }
        }

        var declarationCounts = topLevelFunctions
            .GroupBy(function => function.Name.Lexeme, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var byName = topLevelFunctions
            .Where(function => declarationCounts[function.Name.Lexeme] == 1)
            .ToDictionary(function => function.Name.Lexeme, StringComparer.Ordinal);

        var usage = new UsageCollector(byName);
        foreach (var statement in statements)
            usage.Visit(statement);

        var candidates = new HashSet<Stmt.Function>(topLevelFunctions.Where(function =>
                function.Body is not null &&
                !function.IsAsync && !function.IsGenerator &&
                function.TypeParams is not { Count: > 0 } &&
                function.Decorators is not { Count: > 0 } &&
                function.Parameters.All(parameter =>
                    !parameter.IsRest && !parameter.IsOptional && parameter.DefaultValue is null) &&
                stableFunctions.Contains(function) &&
                declarationCounts[function.Name.Lexeme] == 1 &&
                !exportedNames.Contains(function.Name.Lexeme) &&
                !usage.ValueUsedNames.Contains(function.Name.Lexeme) &&
                usage.CallsByTarget.ContainsKey(function)),
            ReferenceEqualityComparer.Instance);

        foreach (var function in candidates)
        {
            TypeInfo.Function? functionType = typeMap.GetFunctionType(function.Name.Lexeme);
            if (functionType is null)
                continue;

            var assigned = ParameterAssignmentAnalyzer.FindAssigned(function.Body!);
            for (int index = 0;
                 index < function.Parameters.Count && index < functionType.ParamTypes.Count;
                 index++)
            {
                if (!assigned.Contains(function.Parameters[index].Name.Lexeme) &&
                    JsonSerializationShapeAnalyzer.TryGetRecordShape(
                        functionType.ParamTypes[index], out var shape) &&
                    features.CompactObjectRecordShapes.ContainsKey(
                        JsonSerializationShapeAnalyzer.Fingerprint(shape)) &&
                    features.CanAssumeCompactObjectRecordIsUnmaterialized(
                        JsonSerializationShapeAnalyzer.Fingerprint(shape)))
                {
                    if (!exactParameters.TryGetValue(function, out var indices))
                    {
                        indices = [];
                        exactParameters.Add(function, indices);
                    }
                    indices.Add(index);
                }
            }

            bool returnMayBeUndefined = ReturnSlotAnalysis.BlockReturnsMayBeUndefined(
                function.Body, typeMap);
            if (!returnMayBeUndefined &&
                JsonSerializationShapeAnalyzer.TryGetRecordShape(
                    functionType.ReturnType, out var returnShape))
            {
                string fingerprint = JsonSerializationShapeAnalyzer.Fingerprint(returnShape);
                if (features.CompactObjectRecordShapes.ContainsKey(fingerprint) &&
                    features.CanAssumeCompactObjectRecordIsUnmaterialized(fingerprint))
                    exactReturns[function] = fingerprint;
            }
            else if (!returnMayBeUndefined &&
                TryInferReturnFingerprint(function, typeMap, features, out string fingerprint))
            {
                exactReturns[function] = fingerprint;
            }
        }

        // Start optimistic, then remove facts invalidated by any return or call edge.
        // Recursive SCCs remain optimized only when every edge inside and entering the
        // SCC carries the same exact shape.
        bool changed;
        do
        {
            changed = false;
            foreach (var function in candidates)
            {
                if (exactReturns.ContainsKey(function) &&
                    !ValidateReturns(function, byName, typeMap, features,
                        exactParameters, exactReturns))
                {
                    exactReturns.Remove(function);
                    changed = true;
                }

                if (!exactParameters.TryGetValue(function, out var indices))
                    continue;

                TypeInfo.Function functionType = typeMap.GetFunctionType(function.Name.Lexeme)!;
                foreach (int index in indices.ToArray())
                {
                    var parameterShape = GetCompactRecordShape(functionType.ParamTypes[index]);
                    string fingerprint = JsonSerializationShapeAnalyzer.Fingerprint(parameterShape);
                    if (usage.CallsByTarget[function].Any(callSite =>
                            index >= callSite.Call.Arguments.Count ||
                            !IsExactExpression(callSite.Call.Arguments[index], fingerprint,
                                callSite.Caller, byName, typeMap, features,
                                exactParameters, exactReturns)))
                    {
                        indices.Remove(index);
                        changed = true;
                    }
                }
                if (indices.Count == 0)
                    exactParameters.Remove(function);
            }
        } while (changed);
    }

    private static bool ValidateReturns(
        Stmt.Function function,
        IReadOnlyDictionary<string, Stmt.Function> byName,
        TypeMap typeMap,
        RuntimeFeatureSet features,
        IReadOnlyDictionary<Stmt.Function, HashSet<int>> exactParameters,
        IReadOnlyDictionary<Stmt.Function, string> exactReturns)
    {
        string fingerprint = exactReturns[function];
        var collector = new ReturnCollector();
        foreach (var statement in function.Body!)
            collector.Visit(statement);
        return collector.Values.Count > 0 && !collector.HasBareReturn &&
            collector.Values.All(value => IsExactExpression(value, fingerprint, function,
                byName, typeMap, features, exactParameters, exactReturns));
    }

    private static bool IsExactExpression(
        Expr expression,
        string fingerprint,
        Stmt.Function? caller,
        IReadOnlyDictionary<string, Stmt.Function> byName,
        TypeMap typeMap,
        RuntimeFeatureSet features,
        IReadOnlyDictionary<Stmt.Function, HashSet<int>> exactParameters,
        IReadOnlyDictionary<Stmt.Function, string> exactReturns)
    {
        switch (expression)
        {
            case Expr.Literal { Value: null }:
                return true;
            case Expr.Grouping grouping:
                return IsExactExpression(grouping.Expression, fingerprint, caller, byName,
                    typeMap, features, exactParameters, exactReturns);
            case Expr.TypeAssertion assertion:
                return IsExactExpression(assertion.Expression, fingerprint, caller, byName,
                    typeMap, features, exactParameters, exactReturns);
            case Expr.Satisfies satisfies:
                return IsExactExpression(satisfies.Expression, fingerprint, caller, byName,
                    typeMap, features, exactParameters, exactReturns);
            case Expr.NonNullAssertion nonNull:
                return IsExactExpression(nonNull.Expression, fingerprint, caller, byName,
                    typeMap, features, exactParameters, exactReturns);
            case Expr.Ternary ternary:
                return IsExactExpression(ternary.ThenBranch, fingerprint, caller, byName,
                           typeMap, features, exactParameters, exactReturns) &&
                       IsExactExpression(ternary.ElseBranch, fingerprint, caller, byName,
                           typeMap, features, exactParameters, exactReturns);
            case Expr.Call { Callee: Expr.Variable variable, Optional: false }
                when byName.TryGetValue(variable.Name.Lexeme, out var target) &&
                     exactReturns.TryGetValue(target, out string? targetFingerprint):
                return targetFingerprint == fingerprint;
            case Expr.Variable variable when caller is not null:
                return TryGetExactParameterFingerprint(caller, variable.Name.Lexeme,
                    typeMap, exactParameters, out string variableFingerprint) &&
                    variableFingerprint == fingerprint;
            case Expr.Get { Object: Expr.Variable receiver, Optional: false } get
                when caller is not null &&
                     TryGetExactParameterFingerprint(caller, receiver.Name.Lexeme,
                         typeMap, exactParameters, out string receiverFingerprint) &&
                     receiverFingerprint == fingerprint &&
                     features.CompactObjectRecordShapes.TryGetValue(
                         receiverFingerprint, out var receiverShape):
            {
                int fieldIndex = FindField(receiverShape, get.Name.Lexeme);
                return fieldIndex >= 0 &&
                    features.CompactObjectRecordSelfFields.Contains(
                        (receiverFingerprint, fieldIndex));
            }
            case Expr.ObjectLiteral literal:
                if (!JsonSerializationShapeAnalyzer.TryAnalyzeCompactObjectLiteral(
                        literal, typeMap, features.CompactObjectRecordShapes.Values,
                        out var literalShape) ||
                    JsonSerializationShapeAnalyzer.Fingerprint(literalShape) != fingerprint)
                    return false;
                for (int index = 0; index < literal.Properties.Count; index++)
                {
                    if (features.CompactObjectRecordSelfFields.Contains((fingerprint, index)) &&
                        !IsExactExpression(literal.Properties[index].Value, fingerprint, caller,
                            byName, typeMap, features, exactParameters, exactReturns))
                        return false;
                }
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetExactParameterFingerprint(
        Stmt.Function function,
        string parameterName,
        TypeMap typeMap,
        IReadOnlyDictionary<Stmt.Function, HashSet<int>> exactParameters,
        out string fingerprint)
    {
        int index = function.Parameters.FindIndex(parameter =>
            parameter.Name.Lexeme == parameterName);
        if (index >= 0 && exactParameters.TryGetValue(function, out var indices) &&
            indices.Contains(index))
        {
            var functionType = typeMap.GetFunctionType(function.Name.Lexeme);
            if (functionType is not null && index < functionType.ParamTypes.Count &&
                JsonSerializationShapeAnalyzer.TryGetRecordShape(
                    functionType.ParamTypes[index], out var shape))
            {
                fingerprint = JsonSerializationShapeAnalyzer.Fingerprint(shape);
                return true;
            }
        }
        fingerprint = "";
        return false;
    }

    private static bool TryInferReturnFingerprint(
        Stmt.Function function,
        TypeMap typeMap,
        RuntimeFeatureSet features,
        out string fingerprint)
    {
        var collector = new ReturnCollector();
        foreach (var statement in function.Body!)
            collector.Visit(statement);
        string? inferred = null;
        foreach (var value in collector.Values)
        {
            Expr unwrapped = Unwrap(value);
            if (unwrapped is not Expr.ObjectLiteral literal ||
                !JsonSerializationShapeAnalyzer.TryAnalyzeCompactObjectLiteral(
                    literal, typeMap, features.CompactObjectRecordShapes.Values,
                    out var shape))
            {
                fingerprint = "";
                return false;
            }
            string current = JsonSerializationShapeAnalyzer.Fingerprint(shape);
            if (!features.CompactObjectRecordShapes.ContainsKey(current) ||
                !features.CanAssumeCompactObjectRecordIsUnmaterialized(current) ||
                inferred is not null && inferred != current)
            {
                fingerprint = "";
                return false;
            }
            inferred = current;
        }
        fingerprint = inferred ?? "";
        return !collector.HasBareReturn && inferred is not null;
    }

    private static Expr Unwrap(Expr expression) => expression switch
    {
        Expr.Grouping grouping => Unwrap(grouping.Expression),
        Expr.TypeAssertion assertion => Unwrap(assertion.Expression),
        Expr.Satisfies satisfies => Unwrap(satisfies.Expression),
        Expr.NonNullAssertion nonNull => Unwrap(nonNull.Expression),
        _ => expression
    };

    private static int FindField(JsonSerializationShape.Record shape, string name)
    {
        for (int index = 0; index < shape.Fields.Count; index++)
            if (shape.Fields[index].Key == name)
                return index;
        return -1;
    }

    private static JsonSerializationShape.Record GetCompactRecordShape(TypeInfo type)
    {
        JsonSerializationShapeAnalyzer.TryGetRecordShape(type, out var shape);
        return shape;
    }

    private static void CollectTopLevel(
        Stmt statement,
        ICollection<Stmt.Function> functions,
        ISet<string> exportedNames,
        bool exported)
    {
        switch (statement)
        {
            case Stmt.Function function:
                functions.Add(function);
                if (exported)
                    exportedNames.Add(function.Name.Lexeme);
                break;
            case Stmt.Export { Declaration: { } declaration }:
                CollectTopLevel(declaration, functions, exportedNames, exported: true);
                break;
            case Stmt.Sequence sequence:
                foreach (var inner in sequence.Statements)
                    CollectTopLevel(inner, functions, exportedNames, exported);
                break;
        }
    }

    private sealed record CallSite(Stmt.Function? Caller, Expr.Call Call);

    private sealed class UsageCollector(
        IReadOnlyDictionary<string, Stmt.Function> functions) : AstVisitorBase
    {
        private Stmt.Function? _currentFunction;
        public HashSet<string> ValueUsedNames { get; } = new(StringComparer.Ordinal);
        public Dictionary<Stmt.Function, List<CallSite>> CallsByTarget { get; } =
            new(ReferenceEqualityComparer.Instance);

        protected override void VisitFunction(Stmt.Function stmt)
        {
            var previous = _currentFunction;
            _currentFunction = stmt;
            base.VisitFunction(stmt);
            _currentFunction = previous;
        }

        protected override void VisitCall(Expr.Call expr)
        {
            if (expr.Callee is Expr.Variable variable &&
                functions.TryGetValue(variable.Name.Lexeme, out var target) &&
                !expr.Optional && expr.Arguments.All(argument => argument is not Expr.Spread))
            {
                if (!CallsByTarget.TryGetValue(target, out var calls))
                {
                    calls = [];
                    CallsByTarget.Add(target, calls);
                }
                calls.Add(new CallSite(_currentFunction, expr));
                foreach (var argument in expr.Arguments)
                    Visit(argument);
                return;
            }
            base.VisitCall(expr);
        }

        protected override void VisitVariable(Expr.Variable expr)
        {
            if (functions.ContainsKey(expr.Name.Lexeme))
                ValueUsedNames.Add(expr.Name.Lexeme);
        }
    }

    private sealed class ReturnCollector : AstVisitorBase
    {
        public List<Expr> Values { get; } = [];
        public bool HasBareReturn { get; private set; }

        protected override void VisitReturn(Stmt.Return stmt)
        {
            if (stmt.Value is null)
                HasBareReturn = true;
            else
                Values.Add(stmt.Value);
        }

        protected override void VisitFunction(Stmt.Function stmt) { }
        protected override void VisitArrowFunction(Expr.ArrowFunction expr) { }
    }
}
