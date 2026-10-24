using SharpTS.Parsing;
using SharpTS.TypeSystem.Narrowing;

namespace SharpTS.TypeSystem.ControlFlow;

/// <summary>
/// Analyzes loop bodies to find all paths that are assigned within the loop.
/// This is used to invalidate narrowings at the start of loop bodies, since
/// assignments in the loop can affect the validity of narrowings on subsequent iterations.
/// </summary>
public static class LoopAssignmentAnalyzer
{
    /// <summary>
    /// Collects all narrowing paths that are assigned within a statement.
    /// </summary>
    /// <param name="stmt">The statement to analyze (typically a loop body).</param>
    /// <param name="privateOwner">Resolves lexical private names to their declaration identity.</param>
    /// <returns>A set of all paths that receive assignments within the statement.</returns>
    public static HashSet<NarrowingPath> GetAssignedPaths(Stmt stmt, Func<Token, int?>? privateOwner = null)
    {
        var assignedPaths = new HashSet<NarrowingPath>();
        CollectAssignedPaths(stmt, assignedPaths, privateOwner);
        return assignedPaths;
    }

    private static void CollectAssignedPaths(Stmt stmt, HashSet<NarrowingPath> paths, Func<Token, int?>? privateOwner)
    {
        switch (stmt)
        {
            case Stmt.Block block:
                foreach (var s in block.Statements)
                    CollectAssignedPaths(s, paths, privateOwner);
                break;

            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    CollectAssignedPaths(s, paths, privateOwner);
                break;

            case Stmt.Expression exprStmt:
                CollectAssignedPathsFromExpr(exprStmt.Expr, paths, privateOwner);
                break;

            case Stmt.Var varStmt:
                // Variable declaration can be considered an assignment
                if (varStmt.Initializer != null)
                {
                    paths.Add(new NarrowingPath.Variable(varStmt.Name.Lexeme));
                    CollectAssignedPathsFromExpr(varStmt.Initializer, paths, privateOwner);
                }
                break;

            case Stmt.Const constStmt:
                // Const declarations are also assignments
                paths.Add(new NarrowingPath.Variable(constStmt.Name.Lexeme));
                CollectAssignedPathsFromExpr(constStmt.Initializer, paths, privateOwner);
                break;

            case Stmt.If ifStmt:
                CollectAssignedPathsFromExpr(ifStmt.Condition, paths, privateOwner);
                CollectAssignedPaths(ifStmt.ThenBranch, paths, privateOwner);
                if (ifStmt.ElseBranch != null)
                    CollectAssignedPaths(ifStmt.ElseBranch, paths, privateOwner);
                break;

            case Stmt.While whileStmt:
                CollectAssignedPathsFromExpr(whileStmt.Condition, paths, privateOwner);
                CollectAssignedPaths(whileStmt.Body, paths, privateOwner);
                break;

            case Stmt.DoWhile doWhileStmt:
                CollectAssignedPaths(doWhileStmt.Body, paths, privateOwner);
                CollectAssignedPathsFromExpr(doWhileStmt.Condition, paths, privateOwner);
                break;

            case Stmt.For forStmt:
                if (forStmt.Initializer != null)
                    CollectAssignedPaths(forStmt.Initializer, paths, privateOwner);
                if (forStmt.Condition != null)
                    CollectAssignedPathsFromExpr(forStmt.Condition, paths, privateOwner);
                if (forStmt.Increment != null)
                    CollectAssignedPathsFromExpr(forStmt.Increment, paths, privateOwner);
                CollectAssignedPaths(forStmt.Body, paths, privateOwner);
                break;

            case Stmt.ForOf forOfStmt:
                // The loop variable is assigned on each iteration
                paths.Add(new NarrowingPath.Variable(forOfStmt.Variable.Lexeme));
                CollectAssignedPathsFromExpr(forOfStmt.Iterable, paths, privateOwner);
                CollectAssignedPaths(forOfStmt.Body, paths, privateOwner);
                break;

            case Stmt.ForIn forInStmt:
                // The loop variable is assigned on each iteration
                paths.Add(new NarrowingPath.Variable(forInStmt.Variable.Lexeme));
                CollectAssignedPathsFromExpr(forInStmt.Object, paths, privateOwner);
                CollectAssignedPaths(forInStmt.Body, paths, privateOwner);
                break;

            case Stmt.Switch switchStmt:
                CollectAssignedPathsFromExpr(switchStmt.Subject, paths, privateOwner);
                foreach (var caseStmt in switchStmt.Cases)
                {
                    if (caseStmt.Value != null)
                        CollectAssignedPathsFromExpr(caseStmt.Value, paths, privateOwner);
                    foreach (var bodyStmt in caseStmt.Body)
                        CollectAssignedPaths(bodyStmt, paths, privateOwner);
                }
                if (switchStmt.DefaultBody != null)
                {
                    foreach (var bodyStmt in switchStmt.DefaultBody)
                        CollectAssignedPaths(bodyStmt, paths, privateOwner);
                }
                break;

            case Stmt.TryCatch tryCatchStmt:
                foreach (var s in tryCatchStmt.TryBlock)
                    CollectAssignedPaths(s, paths, privateOwner);
                if (tryCatchStmt.CatchBlock != null)
                {
                    if (tryCatchStmt.CatchParam != null)
                        paths.Add(new NarrowingPath.Variable(tryCatchStmt.CatchParam.Lexeme));
                    foreach (var s in tryCatchStmt.CatchBlock)
                        CollectAssignedPaths(s, paths, privateOwner);
                }
                if (tryCatchStmt.FinallyBlock != null)
                {
                    foreach (var s in tryCatchStmt.FinallyBlock)
                        CollectAssignedPaths(s, paths, privateOwner);
                }
                break;

            case Stmt.Return returnStmt:
                if (returnStmt.Value != null)
                    CollectAssignedPathsFromExpr(returnStmt.Value, paths, privateOwner);
                break;

            case Stmt.Throw throwStmt:
                CollectAssignedPathsFromExpr(throwStmt.Value, paths, privateOwner);
                break;

            case Stmt.LabeledStatement labeledStmt:
                CollectAssignedPaths(labeledStmt.Statement, paths, privateOwner);
                break;

            // Other statement types (declarations, imports, etc.) don't contain assignments
            // that we care about for narrowing purposes
        }
    }

    private static void CollectAssignedPathsFromExpr(Expr expr, HashSet<NarrowingPath> paths, Func<Token, int?>? privateOwner)
    {
        switch (expr)
        {
            case Expr.DestructuringAssign destructuring:
                // The destructured targets are written by the lowered assignment statements; collecting
                // their paths keeps loop-narrowing invalidation sound for `[a, b] = ...` in a loop (#754).
                foreach (var stmt in destructuring.Assignments)
                    CollectAssignedPaths(stmt, paths, privateOwner);
                CollectAssignedPathsFromExpr(destructuring.ResultValue, paths, privateOwner);
                break;

            case Expr.Assign assign:
                paths.Add(new NarrowingPath.Variable(assign.Name.Lexeme));
                CollectAssignedPathsFromExpr(assign.Value, paths, privateOwner);
                break;

            case Expr.AssertedAssignment assignment:
                CollectAssignedPathsFromExpr(assignment.LoweredAssignment, paths, privateOwner);
                break;

            case Expr.Set set:
                if (NarrowingPathExtractor.TryExtract(set.Object, privateOwner) is { } setBasePath)
                    paths.Add(new NarrowingPath.PropertyAccess(setBasePath, set.Name.Lexeme));
                CollectAssignedPathsFromExpr(set.Object, paths, privateOwner);
                CollectAssignedPathsFromExpr(set.Value, paths, privateOwner);
                break;

            case Expr.SetPrivate setPrivate:
                AddPrivateAssignmentPath(setPrivate.Object, setPrivate.Name, paths, privateOwner);
                CollectAssignedPathsFromExpr(setPrivate.Object, paths, privateOwner);
                CollectAssignedPathsFromExpr(setPrivate.Value, paths, privateOwner);
                break;

            case Expr.SetSuper setSuper:
                CollectAssignedPathsFromExpr(setSuper.Value, paths, privateOwner);
                break;

            case Expr.SetIndex setIndex:
                CollectAssignedPathsFromExpr(setIndex.Object, paths, privateOwner);
                CollectAssignedPathsFromExpr(setIndex.Index, paths, privateOwner);
                CollectAssignedPathsFromExpr(setIndex.Value, paths, privateOwner);
                if (NarrowingPathExtractor.TryExtract(setIndex.Object, privateOwner) is { } indexBasePath)
                {
                    if (setIndex.Index is Expr.Literal { Value: double d } && d == Math.Floor(d) && d >= 0)
                    {
                        paths.Add(new NarrowingPath.ElementAccess(indexBasePath, (int)d));
                    }
                    else
                    {
                        // Computed index - invalidate entire object
                        paths.Add(indexBasePath);
                    }
                }
                break;

            case Expr.CompoundAssign compound:
                paths.Add(new NarrowingPath.Variable(compound.Name.Lexeme));
                CollectAssignedPathsFromExpr(compound.Value, paths, privateOwner);
                break;

            case Expr.CompoundSet compoundSet:
                if (NarrowingPathExtractor.TryExtract(compoundSet.Object, privateOwner) is { } compoundBasePath)
                    paths.Add(new NarrowingPath.PropertyAccess(compoundBasePath, compoundSet.Name.Lexeme));
                CollectAssignedPathsFromExpr(compoundSet.Object, paths, privateOwner);
                CollectAssignedPathsFromExpr(compoundSet.Value, paths, privateOwner);
                break;

            case Expr.CompoundSetPrivate compoundSetPrivate:
                AddPrivateAssignmentPath(compoundSetPrivate.Object, compoundSetPrivate.Name, paths, privateOwner);
                CollectAssignedPathsFromExpr(compoundSetPrivate.Object, paths, privateOwner);
                CollectAssignedPathsFromExpr(compoundSetPrivate.Value, paths, privateOwner);
                break;

            case Expr.CompoundSetIndex compoundSetIndex:
                CollectAssignedPathsFromExpr(compoundSetIndex.Object, paths, privateOwner);
                CollectAssignedPathsFromExpr(compoundSetIndex.Index, paths, privateOwner);
                CollectAssignedPathsFromExpr(compoundSetIndex.Value, paths, privateOwner);
                if (NarrowingPathExtractor.TryExtract(compoundSetIndex.Object, privateOwner) is { } compoundIndexBasePath)
                {
                    if (compoundSetIndex.Index is Expr.Literal { Value: double d } && d == Math.Floor(d) && d >= 0)
                        paths.Add(new NarrowingPath.ElementAccess(compoundIndexBasePath, (int)d));
                    else
                        paths.Add(compoundIndexBasePath);
                }
                break;

            case Expr.LogicalAssign logicalAssign:
                paths.Add(new NarrowingPath.Variable(logicalAssign.Name.Lexeme));
                CollectAssignedPathsFromExpr(logicalAssign.Value, paths, privateOwner);
                break;

            case Expr.LogicalSet logicalSet:
                if (NarrowingPathExtractor.TryExtract(logicalSet.Object, privateOwner) is { } logicalBasePath)
                    paths.Add(new NarrowingPath.PropertyAccess(logicalBasePath, logicalSet.Name.Lexeme));
                CollectAssignedPathsFromExpr(logicalSet.Object, paths, privateOwner);
                CollectAssignedPathsFromExpr(logicalSet.Value, paths, privateOwner);
                break;

            case Expr.LogicalSetPrivate logicalSetPrivate:
                AddPrivateAssignmentPath(logicalSetPrivate.Object, logicalSetPrivate.Name, paths, privateOwner);
                CollectAssignedPathsFromExpr(logicalSetPrivate.Object, paths, privateOwner);
                CollectAssignedPathsFromExpr(logicalSetPrivate.Value, paths, privateOwner);
                break;

            case Expr.LogicalSetIndex logicalSetIndex:
                CollectAssignedPathsFromExpr(logicalSetIndex.Object, paths, privateOwner);
                CollectAssignedPathsFromExpr(logicalSetIndex.Index, paths, privateOwner);
                CollectAssignedPathsFromExpr(logicalSetIndex.Value, paths, privateOwner);
                if (NarrowingPathExtractor.TryExtract(logicalSetIndex.Object, privateOwner) is { } logicalIndexBasePath)
                {
                    if (logicalSetIndex.Index is Expr.Literal { Value: double d } && d == Math.Floor(d) && d >= 0)
                        paths.Add(new NarrowingPath.ElementAccess(logicalIndexBasePath, (int)d));
                    else
                        paths.Add(logicalIndexBasePath);
                }
                break;

            case Expr.PrefixIncrement prefix:
                CollectAssignedPathsFromExpr(prefix.Operand, paths, privateOwner);
                if (NarrowingPathExtractor.TryExtract(prefix.Operand, privateOwner) is { } prefixPath)
                    paths.Add(prefixPath);
                break;

            case Expr.PostfixIncrement postfix:
                CollectAssignedPathsFromExpr(postfix.Operand, paths, privateOwner);
                if (NarrowingPathExtractor.TryExtract(postfix.Operand, privateOwner) is { } postfixPath)
                    paths.Add(postfixPath);
                break;

            case Expr.Binary binary:
                CollectAssignedPathsFromExpr(binary.Left, paths, privateOwner);
                CollectAssignedPathsFromExpr(binary.Right, paths, privateOwner);
                break;

            case Expr.Logical logical:
                CollectAssignedPathsFromExpr(logical.Left, paths, privateOwner);
                CollectAssignedPathsFromExpr(logical.Right, paths, privateOwner);
                break;

            case Expr.Unary unary:
                CollectAssignedPathsFromExpr(unary.Right, paths, privateOwner);
                break;

            case Expr.Call call:
                CollectAssignedPathsFromExpr(call.Callee, paths, privateOwner);
                foreach (var arg in call.Arguments)
                    CollectAssignedPathsFromExpr(arg, paths, privateOwner);
                break;

            case Expr.Get get:
                CollectAssignedPathsFromExpr(get.Object, paths, privateOwner);
                break;
            case Expr.GetPrivate getPrivate:
                CollectAssignedPathsFromExpr(getPrivate.Object, paths, privateOwner);
                break;
            case Expr.PrivateIn presence:
                CollectAssignedPathsFromExpr(presence.Object, paths, privateOwner);
                break;

            case Expr.GetIndex getIndex:
                CollectAssignedPathsFromExpr(getIndex.Object, paths, privateOwner);
                CollectAssignedPathsFromExpr(getIndex.Index, paths, privateOwner);
                break;

            case Expr.Ternary ternary:
                CollectAssignedPathsFromExpr(ternary.Condition, paths, privateOwner);
                CollectAssignedPathsFromExpr(ternary.ThenBranch, paths, privateOwner);
                CollectAssignedPathsFromExpr(ternary.ElseBranch, paths, privateOwner);
                break;

            case Expr.ArrayLiteral array:
                foreach (var element in array.Elements)
                    CollectAssignedPathsFromExpr(element, paths, privateOwner);
                break;

            case Expr.ObjectLiteral obj:
                foreach (var prop in obj.Properties)
                    CollectAssignedPathsFromExpr(prop.Value, paths, privateOwner);
                break;

            case Expr.NullishCoalescing nullish:
                CollectAssignedPathsFromExpr(nullish.Left, paths, privateOwner);
                CollectAssignedPathsFromExpr(nullish.Right, paths, privateOwner);
                break;

            case Expr.Grouping grouping:
                CollectAssignedPathsFromExpr(grouping.Expression, paths, privateOwner);
                break;

            case Expr.NonNullAssertion nonNull:
                CollectAssignedPathsFromExpr(nonNull.Expression, paths, privateOwner);
                break;

            case Expr.ArrowFunction:
                // Don't descend into arrow functions - they have their own scope
                // Assignments inside don't affect outer narrowings during the loop's execution
                break;

            case Expr.New newExpr:
                foreach (var arg in newExpr.Arguments)
                    CollectAssignedPathsFromExpr(arg, paths, privateOwner);
                break;

            case Expr.TemplateLiteral template:
                foreach (var exprPart in template.Expressions)
                    CollectAssignedPathsFromExpr(exprPart, paths, privateOwner);
                break;

            case Expr.Instantiation instantiation:
                CollectAssignedPathsFromExpr(instantiation.Expression, paths, privateOwner);
                break;
            case Expr.TypeAssertion typeAssertion:
                CollectAssignedPathsFromExpr(typeAssertion.Expression, paths, privateOwner);
                break;

            case Expr.Await awaitExpr:
                CollectAssignedPathsFromExpr(awaitExpr.Expression, paths, privateOwner);
                break;

            case Expr.Spread spread:
                CollectAssignedPathsFromExpr(spread.Expression, paths, privateOwner);
                break;

            // Literals, variables, this, etc. don't contain assignments
        }
    }

    private static void AddPrivateAssignmentPath(
        Expr receiver, Token name, HashSet<NarrowingPath> paths, Func<Token, int?>? privateOwner)
    {
        if (NarrowingPathExtractor.TryExtract(receiver, privateOwner) is not { } basePath) return;
        paths.Add(privateOwner?.Invoke(name) is { } ownerId
            ? new NarrowingPath.PrivateAccess(basePath, ownerId, name.Lexeme)
            : basePath);
    }
}
