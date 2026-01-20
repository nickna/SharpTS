using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Analyzes the AST to identify dead code based on type information and control flow.
/// </summary>
/// <remarks>
/// Runs after TypeChecker and before ILCompiler. Uses the TypeMap to determine
/// which conditions are always true/false, and tracks control flow to find
/// unreachable code after return/throw/break/continue statements.
/// <para>
/// Supports three levels of dead code elimination:
/// <list type="bullet">
/// <item>Level 1: Constant conditions (literal true/false)</item>
/// <item>Level 2: Type-based conditions (typeof checks against known types)</item>
/// <item>Level 3: Control flow (unreachable code after terminators, exhaustive switch)</item>
/// </list>
/// </para>
/// </remarks>
/// <seealso cref="DeadCodeInfo"/>
/// <seealso cref="ILEmitter"/>
public class DeadCodeAnalyzer
{
    private readonly TypeMap _typeMap;
    private readonly DeadCodeInfo _result = new();

    public DeadCodeAnalyzer(TypeMap typeMap)
    {
        _typeMap = typeMap;
    }

    /// <summary>
    /// Analyze the given statements for dead code.
    /// </summary>
    /// <returns>A DeadCodeInfo containing analysis results.</returns>
    public DeadCodeInfo Analyze(List<Stmt> statements)
    {
        AnalyzeBlock(statements);
        return _result;
    }

    #region Statement Analysis

    private void AnalyzeStmt(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.If ifStmt:
                AnalyzeIf(ifStmt);
                break;

            case Stmt.While whileStmt:
                AnalyzeWhile(whileStmt);
                break;

            case Stmt.For forStmt:
                if (forStmt.Initializer != null)
                    AnalyzeStmt(forStmt.Initializer);
                AnalyzeStmt(forStmt.Body);
                break;

            case Stmt.ForOf forOf:
                AnalyzeStmt(forOf.Body);
                break;

            case Stmt.ForIn forIn:
                AnalyzeStmt(forIn.Body);
                break;

            case Stmt.Block block:
                AnalyzeBlock(block.Statements);
                break;

            case Stmt.Sequence seq:
                AnalyzeBlock(seq.Statements);
                break;

            case Stmt.Switch sw:
                AnalyzeSwitch(sw);
                break;

            case Stmt.TryCatch tc:
                AnalyzeTryCatch(tc);
                break;

            case Stmt.Class classStmt:
                foreach (var method in classStmt.Methods)
                {
                    // Skip overload signatures (no body)
                    if (method.Body != null)
                        AnalyzeBlock(method.Body);
                }
                if (classStmt.Accessors != null)
                {
                    foreach (var accessor in classStmt.Accessors)
                        AnalyzeBlock(accessor.Body);
                }
                break;

            case Stmt.Function funcStmt:
                // Skip overload signatures (no body)
                if (funcStmt.Body != null)
                    AnalyzeBlock(funcStmt.Body);
                break;

            case Stmt.Expression exprStmt:
                AnalyzeExpr(exprStmt.Expr);
                break;

            // Terminators and other statements don't need internal analysis
            case Stmt.Return:
            case Stmt.Throw:
            case Stmt.Break:
            case Stmt.Continue:
            case Stmt.Var:
            case Stmt.Const:
            case Stmt.Print:
            case Stmt.Interface:
            case Stmt.TypeAlias:
            case Stmt.Enum:
            case Stmt.Namespace:
                break;

            case Stmt.LabeledStatement labeledStmt:
                AnalyzeStmt(labeledStmt.Statement);
                break;
        }
    }

    private void AnalyzeBlock(List<Stmt> statements)
    {
        bool sawTerminator = false;

        foreach (var stmt in statements)
        {
            if (sawTerminator)
            {
                _result.MarkDead(stmt);
                // Still analyze nested structures for completeness
                AnalyzeStmt(stmt);
                continue;
            }

            AnalyzeStmt(stmt);

            if (DefinitelyTerminates(stmt))
                sawTerminator = true;
        }
    }

    private void AnalyzeIf(Stmt.If ifStmt)
    {
        var conditionResult = EvaluateCondition(ifStmt.Condition);

        if (conditionResult == true)
        {
            // Condition is always true - else branch is dead
            _result.SetIfResult(ifStmt, IfBranchResult.OnlyThenReachable);
            AnalyzeStmt(ifStmt.ThenBranch);
            if (ifStmt.ElseBranch != null)
            {
                _result.MarkDead(ifStmt.ElseBranch);
                AnalyzeStmt(ifStmt.ElseBranch); // Analyze for nested structures
            }
        }
        else if (conditionResult == false)
        {
            // Condition is always false - then branch is dead
            _result.SetIfResult(ifStmt, IfBranchResult.OnlyElseReachable);
            _result.MarkDead(ifStmt.ThenBranch);
            AnalyzeStmt(ifStmt.ThenBranch); // Analyze for nested structures
            if (ifStmt.ElseBranch != null)
                AnalyzeStmt(ifStmt.ElseBranch);
        }
        else
        {
            // Both branches reachable
            _result.SetIfResult(ifStmt, IfBranchResult.BothReachable);
            AnalyzeStmt(ifStmt.ThenBranch);
            if (ifStmt.ElseBranch != null)
                AnalyzeStmt(ifStmt.ElseBranch);
        }
    }

    private void AnalyzeWhile(Stmt.While whileStmt)
    {
        // Check for while(false) - body is dead
        var conditionResult = EvaluateCondition(whileStmt.Condition);
        if (conditionResult == false)
        {
            _result.MarkDead(whileStmt.Body);
        }

        AnalyzeStmt(whileStmt.Body);
    }

    private void AnalyzeSwitch(Stmt.Switch sw)
    {
        var subjectType = _typeMap.Get(sw.Subject);

        // Check for exhaustive switch on union types
        if (subjectType is TypeInfo.Union union)
        {
            var coveredTypes = sw.Cases
                .Where(c => c.Value is Expr.Literal)
                .Select(c => ((Expr.Literal)c.Value).Value?.ToString() ?? "null")
                .ToHashSet();

            bool allCovered = union.FlattenedTypes.All(t => t switch
            {
                TypeInfo.StringLiteral sl => coveredTypes.Contains(sl.Value),
                TypeInfo.NumberLiteral nl => coveredTypes.Contains(nl.Value.ToString()),
                TypeInfo.BooleanLiteral bl => coveredTypes.Contains(bl.Value.ToString()),
                TypeInfo.Null => coveredTypes.Contains("null"),
                _ => false
            });

            if (allCovered)
            {
                _result.SetSwitchResult(sw, new SwitchAnalysis([], DefaultIsUnreachable: true, IsExhaustive: true));
                // Mark default body statements as dead
                if (sw.DefaultBody != null)
                {
                    foreach (var stmt in sw.DefaultBody)
                        _result.MarkDead(stmt);
                }
            }
        }

        // Analyze case bodies for dead code
        foreach (var caseItem in sw.Cases)
            AnalyzeBlock(caseItem.Body);

        if (sw.DefaultBody != null)
            AnalyzeBlock(sw.DefaultBody);
    }

    private void AnalyzeTryCatch(Stmt.TryCatch tc)
    {
        AnalyzeBlock(tc.TryBlock);
        if (tc.CatchBlock != null)
            AnalyzeBlock(tc.CatchBlock);
        if (tc.FinallyBlock != null)
            AnalyzeBlock(tc.FinallyBlock);
    }

    private void AnalyzeExpr(Expr expr)
    {
        // Analyze arrow functions for dead code within them
        if (expr is Expr.ArrowFunction arrow && arrow.BlockBody != null)
        {
            AnalyzeBlock(arrow.BlockBody);
        }
        // Analyze class expressions for dead code within methods
        else if (expr is Expr.ClassExpr classExpr)
        {
            foreach (var method in classExpr.Methods)
            {
                if (method.Body != null)
                    AnalyzeBlock(method.Body);
            }
            if (classExpr.Accessors != null)
            {
                foreach (var accessor in classExpr.Accessors)
                    AnalyzeBlock(accessor.Body);
            }
        }
        // Could add ternary analysis here if needed
    }

    #endregion

    #region Condition Evaluation

    /// <summary>
    /// Evaluate a condition expression to determine if it's always true, always false,
    /// or indeterminate.
    /// </summary>
    /// <returns>true if always true, false if always false, null if indeterminate.</returns>
    private bool? EvaluateCondition(Expr condition)
    {
        // Level 1: Direct boolean literals
        if (condition is Expr.Literal { Value: bool b })
            return b;

        // Grouping - unwrap
        if (condition is Expr.Grouping g)
            return EvaluateCondition(g.Expression);

        // Unary negation
        if (condition is Expr.Unary { Operator.Type: TokenType.BANG } unary)
        {
            var inner = EvaluateCondition(unary.Right);
            return inner.HasValue ? !inner.Value : null;
        }

        // Logical operators with short-circuit evaluation
        if (condition is Expr.Logical log)
        {
            var left = EvaluateCondition(log.Left);
            var right = EvaluateCondition(log.Right);

            if (log.Operator.Type == TokenType.AND_AND)
            {
                // false && anything = false
                if (left == false || right == false) return false;
                // true && true = true
                if (left == true && right == true) return true;
            }
            else // OR
            {
                // true || anything = true
                if (left == true || right == true) return true;
                // false || false = false
                if (left == false && right == false) return false;
            }
            return null;
        }

        // Level 2: typeof checks
        if (condition is Expr.Binary bin)
        {
            if (bin.Operator.Type is TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL)
                return EvaluateTypeofCheck(bin, negate: false);

            if (bin.Operator.Type is TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL)
                return EvaluateTypeofCheck(bin, negate: true);
        }

        return null; // Indeterminate
    }

    /// <summary>
    /// Evaluate a typeof check like `typeof x === "string"`.
    /// </summary>
    private bool? EvaluateTypeofCheck(Expr.Binary bin, bool negate)
    {
        // Pattern: typeof x === "string"
        Expr.Variable? variable = null;
        string? typeofLiteral = null;

        if (bin.Left is Expr.Unary { Operator.Type: TokenType.TYPEOF, Right: Expr.Variable v } &&
            bin.Right is Expr.Literal { Value: string s })
        {
            variable = v;
            typeofLiteral = s;
        }
        // Also handle reversed: "string" === typeof x
        else if (bin.Right is Expr.Unary { Operator.Type: TokenType.TYPEOF, Right: Expr.Variable v2 } &&
                 bin.Left is Expr.Literal { Value: string s2 })
        {
            variable = v2;
            typeofLiteral = s2;
        }

        if (variable == null || typeofLiteral == null)
            return null;

        // Get the type from TypeMap
        var typeInfo = _typeMap.Get(variable);
        if (typeInfo == null)
            return null;

        var result = EvaluateTypeAgainstTypeof(typeInfo, typeofLiteral);
        return negate && result.HasValue ? !result.Value : result;
    }

    /// <summary>
    /// Determine if a type always/never matches a typeof result.
    /// </summary>
    private bool? EvaluateTypeAgainstTypeof(TypeInfo type, string typeofResult)
    {
        // For union types, check if ALL or NONE match
        if (type is TypeInfo.Union union)
        {
            var matches = union.FlattenedTypes.Select(t => TypeMatchesTypeof(t, typeofResult)).ToList();
            if (matches.All(m => m)) return true;   // All match
            if (matches.All(m => !m)) return false; // None match
            return null; // Mixed - indeterminate
        }

        // Single type - definitive answer
        return TypeMatchesTypeof(type, typeofResult);
    }

    /// <summary>
    /// Check if a single type matches a typeof result string.
    /// </summary>
    private static bool TypeMatchesTypeof(TypeInfo type, string typeofResult) => typeofResult switch
    {
        "string" => type is TypeInfo.String or TypeInfo.StringLiteral,
        "number" => type is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } or TypeInfo.NumberLiteral,
        "boolean" => type is TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } or TypeInfo.BooleanLiteral,
        "object" => type is TypeInfo.Null or TypeInfo.Record or TypeInfo.Array or TypeInfo.Instance,
        "function" => type is TypeInfo.Function,
        "undefined" => false, // SharpTS doesn't have undefined as a type
        "symbol" => type is TypeInfo.Symbol,
        _ => false
    };

    #endregion

    #region Control Flow Analysis

    /// <summary>
    /// Returns true if the statement definitely terminates (never falls through).
    /// </summary>
    private bool DefinitelyTerminates(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Return:
            case Stmt.Throw:
            case Stmt.Break:
            case Stmt.Continue:
                return true;

            case Stmt.If ifStmt:
                // Both branches must terminate
                var thenTerminates = DefinitelyTerminates(ifStmt.ThenBranch);
                var elseTerminates = ifStmt.ElseBranch != null && DefinitelyTerminates(ifStmt.ElseBranch);
                return thenTerminates && elseTerminates;

            case Stmt.Block block:
                return block.Statements.Any(DefinitelyTerminates);

            case Stmt.Sequence seq:
                return seq.Statements.Any(DefinitelyTerminates);

            case Stmt.While w when w.Condition is Expr.Literal { Value: true }:
                // while(true) terminates only if there's an unconditional throw/return inside
                return ContainsUnconditionalTerminator(w.Body);

            case Stmt.Switch sw:
                return AnalyzeSwitchTermination(sw);

            case Stmt.TryCatch tc:
                // Terminates if try terminates AND catch terminates (if present)
                var tryTerminates = tc.TryBlock.Any(DefinitelyTerminates);
                var catchTerminates = tc.CatchBlock == null || tc.CatchBlock.Any(DefinitelyTerminates);
                return tryTerminates && catchTerminates;

            case Stmt.LabeledStatement labeled:
                // A labeled statement does NOT terminate just because it contains a break/continue
                // that targets this label - control continues after the labeled statement.
                // Only consider it terminating if it has an unconditional return/throw.
                return ContainsUnconditionalReturnOrThrow(labeled.Statement);

            default:
                return false;
        }
    }

    /// <summary>
    /// Check if a statement contains an unconditional terminator (for while(true) analysis).
    /// </summary>
    private bool ContainsUnconditionalTerminator(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Throw:
            case Stmt.Return:
                return true;

            case Stmt.Block b:
                return b.Statements.Any(ContainsUnconditionalTerminator);

            case Stmt.Sequence s:
                return s.Statements.Any(ContainsUnconditionalTerminator);

            case Stmt.If i:
                return ContainsUnconditionalTerminator(i.ThenBranch) &&
                       (i.ElseBranch != null && ContainsUnconditionalTerminator(i.ElseBranch));

            default:
                return false;
        }
    }

    /// <summary>
    /// Check if a statement contains an unconditional return or throw (ignoring break/continue).
    /// Used for labeled statements where break/continue may exit the label but not terminate.
    /// </summary>
    private bool ContainsUnconditionalReturnOrThrow(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Throw:
            case Stmt.Return:
                return true;

            case Stmt.Block b:
                return b.Statements.Any(ContainsUnconditionalReturnOrThrow);

            case Stmt.Sequence s:
                return s.Statements.Any(ContainsUnconditionalReturnOrThrow);

            case Stmt.If i:
                return ContainsUnconditionalReturnOrThrow(i.ThenBranch) &&
                       (i.ElseBranch != null && ContainsUnconditionalReturnOrThrow(i.ElseBranch));

            case Stmt.LabeledStatement labeled:
                return ContainsUnconditionalReturnOrThrow(labeled.Statement);

            default:
                return false;
        }
    }

    /// <summary>
    /// Analyze if a switch statement definitely terminates.
    /// </summary>
    private bool AnalyzeSwitchTermination(Stmt.Switch sw)
    {
        var analysis = _result.GetSwitchResult(sw);

        // If exhaustive (all union members covered), default is unreachable
        bool hasUnreachableDefault = analysis?.IsExhaustive == true;

        // All reachable cases must terminate
        bool allCasesTerminate = sw.Cases.All(c => c.Body.Any(DefinitelyTerminates));

        // Default must terminate (unless unreachable)
        bool defaultTerminates = hasUnreachableDefault ||
            (sw.DefaultBody != null && sw.DefaultBody.Any(DefinitelyTerminates));

        // If no default and not exhaustive, switch doesn't terminate
        if (sw.DefaultBody == null && !hasUnreachableDefault)
            return false;

        return allCasesTerminate && defaultTerminates;
    }

    #endregion
}
