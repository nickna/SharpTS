using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Analyzes for loops to determine if the loop counter can be kept as an unboxed double.
/// </summary>
/// <remarks>
/// Detects patterns like: for (let i = 0; i &lt; n; i++)
/// Where the loop variable:
/// - Is initialized to a numeric literal
/// - Is not captured by any closure inside the loop body
/// - Is only used in numeric operations (comparisons, increment/decrement)
///
/// When these conditions are met, keeping the counter as a native double
/// eliminates boxing/unboxing overhead on every iteration.
/// </remarks>
public static class ForLoopAnalyzer
{
    /// <summary>
    /// Integer loop-counter optimization gate (#928). When enabled, a provably-integer monotonic
    /// loop counter is backed by a native <c>Int64</c> slot instead of a <c>double</c> — its
    /// increment stays native int and recognized index sites (<c>a[i]</c>, <c>a[i±k]</c>) consume
    /// it directly, closing the typed-array kernel gap to Node. On by default; it is sound (every
    /// value-read materializes back to <c>double</c> via <c>conv.r8</c>, bit-identical to the
    /// double counter, and the byte[] backing is untouched, so .NET interop is preserved). Set
    /// <c>SHARPTS_INT_LOOP_COUNTER=0</c> to disable it as a kill-switch.
    /// </summary>
    public static readonly bool IntegerCounterEnabled =
        Environment.GetEnvironmentVariable("SHARPTS_INT_LOOP_COUNTER") != "0";

    /// <summary>
    /// #928: native int64 modulo for <c>counter % integerLiteral</c>. The integer loop
    /// counter is already an <c>Int64</c> slot; emitting <c>i % 7</c> as an int64 <c>rem</c> (then
    /// <c>conv.r8</c>) instead of an FP <c>fmod</c> on two doubles is bit-identical to JS truncated
    /// remainder for every <c>|i| ≤ 2^53</c> — the same gate the counter representation already
    /// accepts — and the <c>fmod</c> is the dominant per-iteration cost in write kernels (#928
    /// measurement). On by default when the counter is on; kill with <c>SHARPTS_INT_MOD=0</c>.
    /// </summary>
    public static readonly bool IntegerModuloEnabled =
        Environment.GetEnvironmentVariable("SHARPTS_INT_MOD") != "0";

    /// <summary>
    /// Identifies a for-loop counter eligible for the native <c>Int64</c> representation, or null.
    /// Stricter than <see cref="Analyze"/>: requires an INTEGER-literal initializer, a pure
    /// <c>++</c>/<c>--</c> step, a numeric condition, no closure capture, and no reassignment or
    /// re-declaration of the counter anywhere in the body (so it only ever changes by ±1). Unlike
    /// <see cref="Analyze"/> it does NOT bail on an explicit <c>: number</c> annotation — the common
    /// real-world / benchmark form <c>for (let i: number = 0; i &lt; n; i++)</c> must qualify.
    ///
    /// Soundness: TS <c>number</c> is a double, but an integer counter stepping by ±1 stays exactly
    /// representable as both <c>long</c> and <c>double</c> for any loop that terminates in finite
    /// time (≤ 2^53), so reads materialized as <c>conv.r8</c> are bit-identical to today's double
    /// counter. Values only ever observed as doubles; no native-int arithmetic is exposed.
    /// </summary>
    public static string? AnalyzeIntegerCounter(Stmt.For forLoop, ClosureAnalyzer? closureAnalyzer)
    {
        if (forLoop.Initializer is not Stmt.Var varDecl)
            return null;

        string varName = varDecl.Name.Lexeme;

        // Initializer must be an INTEGER literal (0, 1, -1, …).
        if (!TryGetNumericLiteral(varDecl.Initializer, out double initialValue))
            return null;
        if (double.IsNaN(initialValue) || double.IsInfinity(initialValue)
            || initialValue != Math.Truncate(initialValue))
            return null;

        // Step must be exactly ++ or -- on the counter (prototype scope: no += / i = i + k yet).
        if (forLoop.Increment is not (Expr.PostfixIncrement or Expr.PrefixIncrement)
            || !IsSimpleIncDecOf(forLoop.Increment, varName))
            return null;

        // Must not be captured by a closure (a captured counter needs the boxed/cell representation,
        // not a native Int64 slot). A `let`/`const` counter is block-scoped to the loop, so any
        // capturing closure is lexically inside the body / condition / increment — detect that
        // PRECISELY. The global, name-keyed ClosureAnalyzer.IsVariableCaptured bails whenever ANY
        // closure anywhere in the whole bundled program captures a same-named variable, which
        // silently disabled this optimization across multi-module programs (the #928 coverage gap).
        // A `var` counter is function-scoped and may be captured AFTER the loop, where the precise
        // scan can't see it, so keep the conservative global check for that (uncommon) case.
        if (varDecl.IsVar && closureAnalyzer != null && closureAnalyzer.IsVariableCaptured(varName))
            return null;
        if (ContainsPotentialCapture(forLoop.Body, varName)
            || (forLoop.Condition != null && ContainsPotentialCaptureExpr(forLoop.Condition, varName))
            || (forLoop.Increment != null && ContainsPotentialCaptureExpr(forLoop.Increment, varName)))
            return null;

        // Condition must use the counter in numeric comparisons.
        if (forLoop.Condition != null && !IsNumericComparison(forLoop.Condition, varName))
            return null;

        // The counter must change ONLY via the ++/-- increment clause: bail if the body or
        // condition reassigns, compound-assigns, ++/-- s, or re-declares it (a shadowing nested
        // counter). The increment clause itself is the validated ++/-- step, so it is not scanned.
        if (CounterMutatedInBody(forLoop.Body, varName)
            || (forLoop.Condition != null && CounterMutatedInExpr(forLoop.Condition, varName)))
            return null;

        return varName;
    }

    private static bool IsSimpleIncDecOf(Expr increment, string varName) => increment switch
    {
        Expr.PostfixIncrement pi => IsVariableReference(pi.Operand, varName),
        Expr.PrefixIncrement pri => IsVariableReference(pri.Operand, varName),
        _ => false
    };

    private static bool CounterMutatedInBody(Stmt body, string varName)
    {
        var v = new CounterMutationVisitor(varName);
        v.Visit(body);
        return v.Mutated;
    }

    private static bool CounterMutatedInExpr(Expr expr, string varName)
    {
        var v = new CounterMutationVisitor(varName);
        v.VisitExpr(expr);
        return v.Mutated;
    }

    private static bool ContainsPotentialCaptureExpr(Expr expr, string varName)
    {
        var v = new CaptureCheckVisitor(varName);
        v.VisitExpr(expr);
        return v.HasPotentialCapture;
    }

    /// <summary>
    /// Result of analyzing a for loop for unboxed counter optimization.
    /// </summary>
    public readonly struct AnalysisResult
    {
        /// <summary>
        /// Whether the loop counter can safely use unboxed double.
        /// </summary>
        public bool CanUseUnboxedCounter { get; init; }

        /// <summary>
        /// The variable name of the loop counter.
        /// </summary>
        public string? VariableName { get; init; }

        /// <summary>
        /// The initial value of the counter (if known at compile time).
        /// </summary>
        public double? InitialValue { get; init; }

        public static AnalysisResult NotOptimizable => new() { CanUseUnboxedCounter = false };
    }

    /// <summary>
    /// Analyzes a for loop to determine if its counter can be kept unboxed.
    /// </summary>
    /// <param name="forLoop">The for loop to analyze.</param>
    /// <param name="closureAnalyzer">The closure analyzer for checking captured variables.</param>
    /// <returns>Analysis result indicating if optimization is possible.</returns>
    public static AnalysisResult Analyze(Stmt.For forLoop, ClosureAnalyzer? closureAnalyzer)
    {
        // Must have an initializer
        if (forLoop.Initializer is not Stmt.Var varDecl)
            return AnalysisResult.NotOptimizable;

        string varName = varDecl.Name.Lexeme;

        // Check if already has explicit number type (handled by existing CanUseUnboxedLocal)
        if (varDecl.TypeAnnotation == "number")
            return AnalysisResult.NotOptimizable; // Let existing path handle it

        // Initializer must be a numeric literal
        if (!TryGetNumericLiteral(varDecl.Initializer, out double initialValue))
            return AnalysisResult.NotOptimizable;

        // Variable must not be captured by closures
        if (closureAnalyzer != null && closureAnalyzer.IsVariableCaptured(varName))
            return AnalysisResult.NotOptimizable;

        // Check if the loop body contains any closures that might capture the variable
        if (ContainsPotentialCapture(forLoop.Body, varName))
            return AnalysisResult.NotOptimizable;

        // Condition must use the variable in numeric comparisons (or be null)
        if (forLoop.Condition != null && !IsNumericComparison(forLoop.Condition, varName))
            return AnalysisResult.NotOptimizable;

        // Increment must be numeric (++, --, +=, -= with number)
        if (forLoop.Increment != null && !IsNumericIncrement(forLoop.Increment, varName))
            return AnalysisResult.NotOptimizable;

        return new AnalysisResult
        {
            CanUseUnboxedCounter = true,
            VariableName = varName,
            InitialValue = initialValue
        };
    }

    /// <summary>
    /// Tries to extract a numeric literal value from an expression.
    /// </summary>
    private static bool TryGetNumericLiteral(Expr? expr, out double value)
    {
        value = 0;
        if (expr is Expr.Literal { Value: double d })
        {
            value = d;
            return true;
        }
        // Also handle unary minus on a literal (e.g., let i = -1)
        if (expr is Expr.Unary { Operator.Type: TokenType.MINUS, Right: Expr.Literal { Value: double negD } })
        {
            value = -negD;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if the loop body contains any functions that might capture the loop variable.
    /// </summary>
    private static bool ContainsPotentialCapture(Stmt body, string varName)
    {
        var visitor = new CaptureCheckVisitor(varName);
        visitor.Visit(body);
        return visitor.HasPotentialCapture;
    }

    /// <summary>
    /// Checks if the condition is a numeric comparison involving the loop variable.
    /// </summary>
    private static bool IsNumericComparison(Expr condition, string varName)
    {
        // Accept: i < n, i <= n, i > n, i >= n, n > i, etc.
        if (condition is Expr.Binary binary)
        {
            if (!IsComparisonOperator(binary.Operator.Type))
                return false;

            // At least one side must reference the variable
            bool leftIsVar = IsVariableReference(binary.Left, varName);
            bool rightIsVar = IsVariableReference(binary.Right, varName);

            if (!leftIsVar && !rightIsVar)
                return false;

            // The other side should be a numeric expression
            if (leftIsVar && !IsNumericExpression(binary.Right))
                return false;
            if (rightIsVar && !IsNumericExpression(binary.Left))
                return false;

            return true;
        }

        // Also allow logical combinations: i < n && i >= 0
        if (condition is Expr.Logical logical)
        {
            return IsNumericComparison(logical.Left, varName) &&
                   IsNumericComparison(logical.Right, varName);
        }

        return false;
    }

    private static bool IsComparisonOperator(TokenType type)
    {
        return type is TokenType.LESS or TokenType.LESS_EQUAL
            or TokenType.GREATER or TokenType.GREATER_EQUAL
            or TokenType.EQUAL_EQUAL or TokenType.EQUAL_EQUAL_EQUAL
            or TokenType.BANG_EQUAL or TokenType.BANG_EQUAL_EQUAL;
    }

    /// <summary>
    /// Checks if an expression is a reference to the specified variable.
    /// </summary>
    private static bool IsVariableReference(Expr expr, string varName)
    {
        return expr is Expr.Variable v && v.Name.Lexeme == varName;
    }

    /// <summary>
    /// Checks if an expression is likely numeric (conservative check).
    /// </summary>
    private static bool IsNumericExpression(Expr expr)
    {
        return expr switch
        {
            Expr.Literal { Value: double } => true,
            Expr.Variable => true, // Variables are allowed (e.g., i < n)
            Expr.Binary b when IsArithmeticOperator(b.Operator.Type) => true,
            Expr.Unary u when u.Operator.Type == TokenType.MINUS => true,
            Expr.Get => true, // Property access (e.g., arr.length)
            Expr.Grouping g => IsNumericExpression(g.Expression),
            _ => false
        };
    }

    private static bool IsArithmeticOperator(TokenType type)
    {
        return type is TokenType.PLUS or TokenType.MINUS
            or TokenType.STAR or TokenType.SLASH
            or TokenType.PERCENT or TokenType.STAR_STAR;
    }

    /// <summary>
    /// Checks if the increment is a numeric operation on the loop variable.
    /// </summary>
    private static bool IsNumericIncrement(Expr increment, string varName)
    {
        // i++ (increment or decrement, distinguished by operator)
        if (increment is Expr.PostfixIncrement pi && IsVariableReference(pi.Operand, varName))
            return true;

        // ++i or --i (increment or decrement, distinguished by operator)
        if (increment is Expr.PrefixIncrement pri && IsVariableReference(pri.Operand, varName))
            return true;

        // i += n or i -= n
        if (increment is Expr.CompoundAssign ca && ca.Name.Lexeme == varName)
        {
            if (ca.Operator.Type is TokenType.PLUS_EQUAL or TokenType.MINUS_EQUAL)
                return IsNumericExpression(ca.Value);
        }

        // i = i + n or i = i - n
        if (increment is Expr.Assign assign && assign.Name.Lexeme == varName)
        {
            if (assign.Value is Expr.Binary b &&
                b.Operator.Type is TokenType.PLUS or TokenType.MINUS &&
                IsVariableReference(b.Left, varName))
            {
                return IsNumericExpression(b.Right);
            }
        }

        return false;
    }

    /// <summary>
    /// Visitor that checks if a statement/expression contains functions that reference a variable.
    /// </summary>
    private class CaptureCheckVisitor : Parsing.Visitors.AstVisitorBase
    {
        private readonly string _varName;
        private bool _insideFunction;

        public bool HasPotentialCapture { get; private set; }

        public CaptureCheckVisitor(string varName)
        {
            _varName = varName;
        }

        public void VisitExpr(Expr expr) => Visit(expr);

        protected override void VisitArrowFunction(Expr.ArrowFunction expr)
        {
            // Enter a function scope and check for references
            var wasInside = _insideFunction;
            _insideFunction = true;
            base.VisitArrowFunction(expr);
            _insideFunction = wasInside;
        }

        protected override void VisitFunction(Stmt.Function stmt)
        {
            // Function declarations inside loops can capture variables
            var wasInside = _insideFunction;
            _insideFunction = true;
            base.VisitFunction(stmt);
            _insideFunction = wasInside;
        }

        protected override void VisitVariable(Expr.Variable expr)
        {
            if (_insideFunction && expr.Name.Lexeme == _varName)
            {
                HasPotentialCapture = true;
            }
        }

        protected override void VisitAssign(Expr.Assign expr)
        {
            if (_insideFunction && expr.Name.Lexeme == _varName)
            {
                HasPotentialCapture = true;
            }
            base.VisitAssign(expr);
        }
    }

    /// <summary>
    /// Flags any mutation of the named counter inside a loop body: assignment, compound-assignment,
    /// ++/--, or re-declaration (a shadowing nested-loop counter of the same name). Used by
    /// <see cref="AnalyzeIntegerCounter"/> to guarantee the counter only ever changes by the
    /// loop's ±1 increment clause, so the native Int64 representation can never observe a
    /// non-integer or out-of-step value.
    /// </summary>
    private class CounterMutationVisitor : Parsing.Visitors.AstVisitorBase
    {
        private readonly string _varName;
        public bool Mutated { get; private set; }

        public CounterMutationVisitor(string varName) => _varName = varName;

        public void VisitExpr(Expr expr) => Visit(expr);

        protected override void VisitAssign(Expr.Assign expr)
        {
            if (expr.Name.Lexeme == _varName) Mutated = true;
            base.VisitAssign(expr);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
        {
            if (expr.Name.Lexeme == _varName) Mutated = true;
            base.VisitCompoundAssign(expr);
        }

        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expr)
        {
            if (IsVariableReference(expr.Operand, _varName)) Mutated = true;
            base.VisitPrefixIncrement(expr);
        }

        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expr)
        {
            if (IsVariableReference(expr.Operand, _varName)) Mutated = true;
            base.VisitPostfixIncrement(expr);
        }

        protected override void VisitVar(Stmt.Var stmt)
        {
            if (stmt.Name.Lexeme == _varName) Mutated = true;
            base.VisitVar(stmt);
        }
    }
}
