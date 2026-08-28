namespace SharpTS.Parsing;

/// <summary>
/// Implements JavaScript <c>var</c> hoisting as a parser-time AST transform.
/// </summary>
/// <remarks>
/// JavaScript <c>var</c> declarations are function-scoped and hoisted to the top of the
/// enclosing function (or top-level for module/script bodies). This means:
/// <code>
/// function f() {
///   if (cond) { var x = 1; }
///   return x; // visible — Node returns 1 or undefined, never ReferenceError
/// }
/// </code>
///
/// Rather than implementing function-scope semantics in the interpreter and IL compiler
/// directly, we transform the AST at parse time:
/// <list type="number">
/// <item>Walk all statements in a function/module body, recursing into nested blocks but NOT
/// into nested function declarations or function expressions (each has its own var scope).</item>
/// <item>Collect all <c>Stmt.Var</c> nodes whose <c>IsVar</c> flag is true.</item>
/// <item>Rewrite each in-place: <c>var x = expr</c> becomes a plain assignment expression
/// statement <c>x = expr</c>; <c>var x;</c> (no initializer) becomes a no-op.</item>
/// <item>Prepend synthetic <c>var x;</c> declarations at the top of the body (one per unique
/// hoisted name). These declare the binding in the function scope.</item>
/// </list>
///
/// After this pass the interpreter and IL compiler can treat the result as already-correctly-
/// scoped <c>let</c>-style declarations — no new runtime semantics needed.
///
/// <para>Limitations:</para>
/// <list type="bullet">
/// <item>Arrow function and function expression bodies are hoisted at their parse sites
/// in Parser.Expressions.cs.</item>
/// <item>Duplicate <c>var</c> declarations of the same name at the top level are deduplicated:
/// the first is kept and subsequent ones are rewritten to plain assignments.</item>
/// <item>Does not currently descend into class bodies, getters, setters, or constructors;
/// those rely on being separate function scopes already.</item>
/// </list>
/// </remarks>
public static class VarHoister
{
    /// <summary>
    /// Hoists <c>var</c> declarations within the given body. Returns a new statement list
    /// with synthetic declarations at the top and original declarations rewritten as
    /// assignments. If no <c>var</c> declarations exist, returns the input list unchanged
    /// (a fast-path that avoids allocations for the common case).
    /// </summary>
    /// <param name="spans">
    /// When supplied, rewritten statements inherit the source position of the statements they
    /// replace, and the synthetic declarations prepended to the body are marked compiler-generated
    /// so a debugger steps over them instead of jumping back to the original <c>var</c>.
    /// </param>
    public static List<Stmt> Hoist(List<Stmt> body, SpanTable? spans = null)
    {
        var collected = new List<(Token Name, string? TypeAnnotation, TypeNode? TypeAnnotationNode, Expr? InitializerForInference)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rewritten = new List<Stmt>(body.Count);
        bool changed = false;

        foreach (var stmt in body)
        {
            var newStmt = RewriteAndCollect(stmt, collected, seen, spans, isTopLevel: true, ref changed);
            rewritten.Add(newStmt);
        }

        if (collected.Count == 0)
        {
            return changed ? rewritten : body;
        }

        // Prepend synthetic `var name;` declarations for each unique hoisted name.
        // Carry the first-seen annotation so the type checker can detect TS2403 when a
        // later declaration uses a different annotation (e.g. nested string then top-level number).
        // When the first declaration had no annotation but an initializer, carry the initializer
        // (for type inference only) so the binding's declared type is the initializer's type rather
        // than `any` — otherwise a later `var z: number;` / `var z = 5;` would not report TS2403.
        var result = new List<Stmt>(collected.Count + rewritten.Count);
        foreach (var (nameToken, typeAnnotation, typeAnnotationNode, initializerForInference) in collected)
        {
            var hoisted = new Stmt.Var(nameToken, TypeAnnotation: typeAnnotation, TypeAnnotationNode: typeAnnotationNode, Initializer: null, HasDefiniteAssignmentAssertion: false, IsVar: true, HoistTypeInferenceInitializer: initializerForInference);
            // The binding is declared at the top of the body, not where the user wrote `var`.
            // Pointing it back at the original text would make stepping jump backwards.
            spans?.MarkHidden(hoisted);
            result.Add(hoisted);
        }
        result.AddRange(rewritten);
        return result;
    }

    /// <summary>
    /// Recursively walks a statement, collecting hoisted var names and rewriting var
    /// declarations as assignments. Returns the (possibly rewritten) statement.
    /// </summary>
    /// <param name="isTopLevel">True if this statement is directly inside the function/module
    /// body. Top-level vars are still rewritten to assignments (so the synthetic declarations
    /// at the top can hold the binding) but they appear in the same source-order position.</param>
    private static Stmt RewriteAndCollect(Stmt stmt, List<(Token Name, string? TypeAnnotation, TypeNode? TypeAnnotationNode, Expr? InitializerForInference)> collected, HashSet<string> seen, SpanTable? spans, bool isTopLevel, ref bool changed)
    {
        Stmt result = RewriteAndCollectCore(stmt, collected, seen, spans, isTopLevel, ref changed);

        // Hoisting rebuilds every enclosing statement it walks through. Each rebuilt node stands for
        // the same source the user wrote, so it inherits that position — otherwise a `var` anywhere
        // in a function would strip spans from every statement around it.
        spans?.CopySpan(stmt, result);
        return result;
    }

    private static Stmt RewriteAndCollectCore(Stmt stmt, List<(Token Name, string? TypeAnnotation, TypeNode? TypeAnnotationNode, Expr? InitializerForInference)> collected, HashSet<string> seen, SpanTable? spans, bool isTopLevel, ref bool changed)
    {
        switch (stmt)
        {
            case Stmt.Var v when v.IsVar:
            {
                // Top-level vars: keep the first declaration, rewrite duplicates to assignments.
                // JavaScript allows `var x = 1; var x = 2;` — duplicates silently merge.
                if (isTopLevel)
                {
                    if (seen.Add(v.Name.Lexeme))
                    {
                        return v; // first occurrence: keep the declaration
                    }
                    // Duplicate top-level var: rewrite to assignment
                    changed = true;
                    if (v.Initializer == null)
                    {
                        return RewriteAnnotationOnlyDuplicate(v);
                    }
                    return new Stmt.Expression(new Expr.Assign(
                        v.Name,
                        v.Initializer,
                        IsVarRedeclaration: true,
                        RedeclarationTypeAnnotation: v.TypeAnnotation,
                        RedeclarationTypeAnnotationNode: v.TypeAnnotationNode));
                }

                if (seen.Add(v.Name.Lexeme))
                {
                    // Carry the initializer for type inference only when there is no annotation
                    // to carry — an explicit annotation already establishes the declared type.
                    Expr? initForInference = v.TypeAnnotation == null && v.TypeAnnotationNode == null
                        ? v.Initializer
                        : null;
                    collected.Add((v.Name, v.TypeAnnotation, v.TypeAnnotationNode, initForInference));
                }
                changed = true;
                if (v.Initializer == null)
                {
                    // `var x;` → no-op (the synthetic declaration handles binding creation)
                    return RewriteAnnotationOnlyDuplicate(v);
                }
                // `var x = expr` → `x = expr;`
                return new Stmt.Expression(new Expr.Assign(
                    v.Name,
                    v.Initializer,
                    IsVarRedeclaration: true,
                    RedeclarationTypeAnnotation: v.TypeAnnotation,
                    RedeclarationTypeAnnotationNode: v.TypeAnnotationNode));
            }

            case Stmt.Sequence seq:
            {
                var newStmts = new List<Stmt>(seq.Statements.Count);
                bool seqChanged = false;
                foreach (var inner in seq.Statements)
                {
                    var rewritten = RewriteAndCollect(inner, collected, seen, spans, isTopLevel, ref seqChanged);
                    newStmts.Add(rewritten);
                }
                if (seqChanged)
                {
                    changed = true;
                    return new Stmt.Sequence(newStmts);
                }
                return seq;
            }

            case Stmt.Block block:
            {
                var newStmts = new List<Stmt>(block.Statements.Count);
                bool blockChanged = false;
                foreach (var inner in block.Statements)
                {
                    var rewritten = RewriteAndCollect(inner, collected, seen, spans, isTopLevel: false, ref blockChanged);
                    newStmts.Add(rewritten);
                }
                if (blockChanged)
                {
                    changed = true;
                    return new Stmt.Block(newStmts);
                }
                return block;
            }

            case Stmt.If ifStmt:
            {
                bool ifChanged = false;
                var newThen = RewriteAndCollect(ifStmt.ThenBranch, collected, seen, spans, isTopLevel: false, ref ifChanged);
                Stmt? newElse = ifStmt.ElseBranch != null
                    ? RewriteAndCollect(ifStmt.ElseBranch, collected, seen, spans, isTopLevel: false, ref ifChanged)
                    : null;
                if (ifChanged)
                {
                    changed = true;
                    return new Stmt.If(ifStmt.Condition, newThen, newElse);
                }
                return ifStmt;
            }

            case Stmt.While whileStmt:
            {
                bool whileChanged = false;
                var newBody = RewriteAndCollect(whileStmt.Body, collected, seen, spans, isTopLevel: false, ref whileChanged);
                if (whileChanged)
                {
                    changed = true;
                    return new Stmt.While(whileStmt.Condition, newBody);
                }
                return whileStmt;
            }

            case Stmt.DoWhile doWhile:
            {
                bool dwChanged = false;
                var newBody = RewriteAndCollect(doWhile.Body, collected, seen, spans, isTopLevel: false, ref dwChanged);
                if (dwChanged)
                {
                    changed = true;
                    return new Stmt.DoWhile(newBody, doWhile.Condition);
                }
                return doWhile;
            }

            case Stmt.For forStmt:
            {
                bool forChanged = false;
                Stmt? newInit = forStmt.Initializer != null
                    ? RewriteAndCollect(forStmt.Initializer, collected, seen, spans, isTopLevel: false, ref forChanged)
                    : null;
                var newBody = RewriteAndCollect(forStmt.Body, collected, seen, spans, isTopLevel: false, ref forChanged);
                if (forChanged)
                {
                    changed = true;
                    return new Stmt.For(newInit, forStmt.Condition, forStmt.Increment, newBody);
                }
                return forStmt;
            }

            case Stmt.ForOf forOf:
            {
                bool fofChanged = false;
                var newBody = RewriteAndCollect(forOf.Body, collected, seen, spans, isTopLevel: false, ref fofChanged);
                if (fofChanged)
                {
                    changed = true;
                    return forOf with { Body = newBody };
                }
                return forOf;
            }

            case Stmt.ForIn forIn:
            {
                bool finChanged = false;
                var newBody = RewriteAndCollect(forIn.Body, collected, seen, spans, isTopLevel: false, ref finChanged);
                if (finChanged)
                {
                    changed = true;
                    return forIn with { Body = newBody };
                }
                return forIn;
            }

            case Stmt.LabeledStatement labeled:
            {
                bool lblChanged = false;
                var newInner = RewriteAndCollect(labeled.Statement, collected, seen, spans, isTopLevel: false, ref lblChanged);
                if (lblChanged)
                {
                    changed = true;
                    return new Stmt.LabeledStatement(labeled.Label, newInner);
                }
                return labeled;
            }

            case Stmt.TryCatch tryCatch:
            {
                bool tcChanged = false;
                var newTry = RewriteList(tryCatch.TryBlock, collected, seen, spans, ref tcChanged);
                List<Stmt>? newCatch = tryCatch.CatchBlock != null
                    ? RewriteList(tryCatch.CatchBlock, collected, seen, spans, ref tcChanged)
                    : null;
                List<Stmt>? newFinally = tryCatch.FinallyBlock != null
                    ? RewriteList(tryCatch.FinallyBlock, collected, seen, spans, ref tcChanged)
                    : null;
                if (tcChanged)
                {
                    changed = true;
                    return tryCatch with { TryBlock = newTry, CatchBlock = newCatch, FinallyBlock = newFinally };
                }
                return tryCatch;
            }

            case Stmt.Switch switchStmt:
            {
                bool swChanged = false;
                var newCases = new List<Stmt.SwitchCase>(switchStmt.Cases.Count);
                foreach (var c in switchStmt.Cases)
                {
                    var newCaseStmts = RewriteList(c.Body, collected, seen, spans, ref swChanged);
                    newCases.Add(new Stmt.SwitchCase(c.Value, newCaseStmts));
                }
                List<Stmt>? newDefault = switchStmt.DefaultBody != null
                    ? RewriteList(switchStmt.DefaultBody, collected, seen, spans, ref swChanged)
                    : null;
                if (swChanged)
                {
                    changed = true;
                    return switchStmt with { Cases = newCases, DefaultBody = newDefault };
                }
                return switchStmt;
            }

            // Don't recurse into nested function declarations — they have their own var scope.
            // Top-level Stmt.Function will be hoisted on its own (when its body is parsed).
            case Stmt.Function:
                return stmt;

            default:
                return stmt;
        }
    }

    /// <summary>
    /// Rewrites a duplicate annotation-only <c>var</c> (<c>var x: T;</c> with no initializer) whose
    /// binding already exists. Without a type annotation there is nothing to verify, so it collapses
    /// to a no-op. With an annotation, it becomes a synthesized self-assignment <c>x = x</c> carrying
    /// the annotation: a runtime no-op that preserves the existing value, but a checkable node the
    /// type checker uses to report TS2403 when the annotation differs from the established type.
    /// </summary>
    private static Stmt RewriteAnnotationOnlyDuplicate(Stmt.Var v)
    {
        if (v.TypeAnnotation == null && v.TypeAnnotationNode == null)
        {
            return new Stmt.Expression(new Expr.Literal(null));
        }
        return new Stmt.Expression(new Expr.Assign(
            v.Name,
            new Expr.Variable(v.Name),
            IsVarRedeclaration: true,
            RedeclarationTypeAnnotation: v.TypeAnnotation,
            RedeclarationTypeAnnotationNode: v.TypeAnnotationNode));
    }

    /// <summary>
    /// Helper for rewriting a list of statements (used by TryCatch/Switch which carry
    /// <c>List&lt;Stmt&gt;</c> directly rather than wrapping in a Block).
    /// </summary>
    private static List<Stmt> RewriteList(List<Stmt> list, List<(Token Name, string? TypeAnnotation, TypeNode? TypeAnnotationNode, Expr? InitializerForInference)> collected, HashSet<string> seen, SpanTable? spans, ref bool changed)
    {
        var result = new List<Stmt>(list.Count);
        foreach (var s in list)
        {
            result.Add(RewriteAndCollect(s, collected, seen, spans, isTopLevel: false, ref changed));
        }
        return result;
    }
}
