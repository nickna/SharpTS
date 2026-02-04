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
    /// <returns>A set of all paths that receive assignments within the statement.</returns>
    public static HashSet<NarrowingPath> GetAssignedPaths(Stmt stmt)
    {
        var assignedPaths = new HashSet<NarrowingPath>();
        CollectAssignedPaths(stmt, assignedPaths);
        return assignedPaths;
    }

    private static void CollectAssignedPaths(Stmt stmt, HashSet<NarrowingPath> paths)
    {
        switch (stmt)
        {
            case Stmt.Block block:
                foreach (var s in block.Statements)
                    CollectAssignedPaths(s, paths);
                break;

            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    CollectAssignedPaths(s, paths);
                break;

            case Stmt.Expression exprStmt:
                CollectAssignedPathsFromExpr(exprStmt.Expr, paths);
                break;

            case Stmt.Var varStmt:
                // Variable declaration can be considered an assignment
                if (varStmt.Initializer != null)
                {
                    paths.Add(new NarrowingPath.Variable(varStmt.Name.Lexeme));
                    CollectAssignedPathsFromExpr(varStmt.Initializer, paths);
                }
                break;

            case Stmt.Const constStmt:
                // Const declarations are also assignments
                paths.Add(new NarrowingPath.Variable(constStmt.Name.Lexeme));
                CollectAssignedPathsFromExpr(constStmt.Initializer, paths);
                break;

            case Stmt.If ifStmt:
                CollectAssignedPathsFromExpr(ifStmt.Condition, paths);
                CollectAssignedPaths(ifStmt.ThenBranch, paths);
                if (ifStmt.ElseBranch != null)
                    CollectAssignedPaths(ifStmt.ElseBranch, paths);
                break;

            case Stmt.While whileStmt:
                CollectAssignedPathsFromExpr(whileStmt.Condition, paths);
                CollectAssignedPaths(whileStmt.Body, paths);
                break;

            case Stmt.DoWhile doWhileStmt:
                CollectAssignedPaths(doWhileStmt.Body, paths);
                CollectAssignedPathsFromExpr(doWhileStmt.Condition, paths);
                break;

            case Stmt.For forStmt:
                if (forStmt.Initializer != null)
                    CollectAssignedPaths(forStmt.Initializer, paths);
                if (forStmt.Condition != null)
                    CollectAssignedPathsFromExpr(forStmt.Condition, paths);
                if (forStmt.Increment != null)
                    CollectAssignedPathsFromExpr(forStmt.Increment, paths);
                CollectAssignedPaths(forStmt.Body, paths);
                break;

            case Stmt.ForOf forOfStmt:
                // The loop variable is assigned on each iteration
                paths.Add(new NarrowingPath.Variable(forOfStmt.Variable.Lexeme));
                CollectAssignedPathsFromExpr(forOfStmt.Iterable, paths);
                CollectAssignedPaths(forOfStmt.Body, paths);
                break;

            case Stmt.ForIn forInStmt:
                // The loop variable is assigned on each iteration
                paths.Add(new NarrowingPath.Variable(forInStmt.Variable.Lexeme));
                CollectAssignedPathsFromExpr(forInStmt.Object, paths);
                CollectAssignedPaths(forInStmt.Body, paths);
                break;

            case Stmt.Switch switchStmt:
                CollectAssignedPathsFromExpr(switchStmt.Subject, paths);
                foreach (var caseStmt in switchStmt.Cases)
                {
                    if (caseStmt.Value != null)
                        CollectAssignedPathsFromExpr(caseStmt.Value, paths);
                    foreach (var bodyStmt in caseStmt.Body)
                        CollectAssignedPaths(bodyStmt, paths);
                }
                if (switchStmt.DefaultBody != null)
                {
                    foreach (var bodyStmt in switchStmt.DefaultBody)
                        CollectAssignedPaths(bodyStmt, paths);
                }
                break;

            case Stmt.TryCatch tryCatchStmt:
                foreach (var s in tryCatchStmt.TryBlock)
                    CollectAssignedPaths(s, paths);
                if (tryCatchStmt.CatchBlock != null)
                {
                    if (tryCatchStmt.CatchParam != null)
                        paths.Add(new NarrowingPath.Variable(tryCatchStmt.CatchParam.Lexeme));
                    foreach (var s in tryCatchStmt.CatchBlock)
                        CollectAssignedPaths(s, paths);
                }
                if (tryCatchStmt.FinallyBlock != null)
                {
                    foreach (var s in tryCatchStmt.FinallyBlock)
                        CollectAssignedPaths(s, paths);
                }
                break;

            case Stmt.Return returnStmt:
                if (returnStmt.Value != null)
                    CollectAssignedPathsFromExpr(returnStmt.Value, paths);
                break;

            case Stmt.Throw throwStmt:
                CollectAssignedPathsFromExpr(throwStmt.Value, paths);
                break;

            case Stmt.LabeledStatement labeledStmt:
                CollectAssignedPaths(labeledStmt.Statement, paths);
                break;

            // Other statement types (declarations, imports, etc.) don't contain assignments
            // that we care about for narrowing purposes
        }
    }

    private static void CollectAssignedPathsFromExpr(Expr expr, HashSet<NarrowingPath> paths)
    {
        switch (expr)
        {
            case Expr.Assign assign:
                paths.Add(new NarrowingPath.Variable(assign.Name.Lexeme));
                CollectAssignedPathsFromExpr(assign.Value, paths);
                break;

            case Expr.Set set:
                if (NarrowingPathExtractor.TryExtract(set.Object) is { } setBasePath)
                    paths.Add(new NarrowingPath.PropertyAccess(setBasePath, set.Name.Lexeme));
                CollectAssignedPathsFromExpr(set.Object, paths);
                CollectAssignedPathsFromExpr(set.Value, paths);
                break;

            case Expr.SetIndex setIndex:
                CollectAssignedPathsFromExpr(setIndex.Object, paths);
                CollectAssignedPathsFromExpr(setIndex.Index, paths);
                CollectAssignedPathsFromExpr(setIndex.Value, paths);
                if (NarrowingPathExtractor.TryExtract(setIndex.Object) is { } indexBasePath)
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
                CollectAssignedPathsFromExpr(compound.Value, paths);
                break;

            case Expr.CompoundSet compoundSet:
                if (NarrowingPathExtractor.TryExtract(compoundSet.Object) is { } compoundBasePath)
                    paths.Add(new NarrowingPath.PropertyAccess(compoundBasePath, compoundSet.Name.Lexeme));
                CollectAssignedPathsFromExpr(compoundSet.Object, paths);
                CollectAssignedPathsFromExpr(compoundSet.Value, paths);
                break;

            case Expr.CompoundSetIndex compoundSetIndex:
                CollectAssignedPathsFromExpr(compoundSetIndex.Object, paths);
                CollectAssignedPathsFromExpr(compoundSetIndex.Index, paths);
                CollectAssignedPathsFromExpr(compoundSetIndex.Value, paths);
                if (NarrowingPathExtractor.TryExtract(compoundSetIndex.Object) is { } compoundIndexBasePath)
                {
                    if (compoundSetIndex.Index is Expr.Literal { Value: double d } && d == Math.Floor(d) && d >= 0)
                        paths.Add(new NarrowingPath.ElementAccess(compoundIndexBasePath, (int)d));
                    else
                        paths.Add(compoundIndexBasePath);
                }
                break;

            case Expr.LogicalAssign logicalAssign:
                paths.Add(new NarrowingPath.Variable(logicalAssign.Name.Lexeme));
                CollectAssignedPathsFromExpr(logicalAssign.Value, paths);
                break;

            case Expr.LogicalSet logicalSet:
                if (NarrowingPathExtractor.TryExtract(logicalSet.Object) is { } logicalBasePath)
                    paths.Add(new NarrowingPath.PropertyAccess(logicalBasePath, logicalSet.Name.Lexeme));
                CollectAssignedPathsFromExpr(logicalSet.Object, paths);
                CollectAssignedPathsFromExpr(logicalSet.Value, paths);
                break;

            case Expr.LogicalSetIndex logicalSetIndex:
                CollectAssignedPathsFromExpr(logicalSetIndex.Object, paths);
                CollectAssignedPathsFromExpr(logicalSetIndex.Index, paths);
                CollectAssignedPathsFromExpr(logicalSetIndex.Value, paths);
                if (NarrowingPathExtractor.TryExtract(logicalSetIndex.Object) is { } logicalIndexBasePath)
                {
                    if (logicalSetIndex.Index is Expr.Literal { Value: double d } && d == Math.Floor(d) && d >= 0)
                        paths.Add(new NarrowingPath.ElementAccess(logicalIndexBasePath, (int)d));
                    else
                        paths.Add(logicalIndexBasePath);
                }
                break;

            case Expr.PrefixIncrement prefix:
                CollectAssignedPathsFromExpr(prefix.Operand, paths);
                if (NarrowingPathExtractor.TryExtract(prefix.Operand) is { } prefixPath)
                    paths.Add(prefixPath);
                break;

            case Expr.PostfixIncrement postfix:
                CollectAssignedPathsFromExpr(postfix.Operand, paths);
                if (NarrowingPathExtractor.TryExtract(postfix.Operand) is { } postfixPath)
                    paths.Add(postfixPath);
                break;

            case Expr.Binary binary:
                CollectAssignedPathsFromExpr(binary.Left, paths);
                CollectAssignedPathsFromExpr(binary.Right, paths);
                break;

            case Expr.Logical logical:
                CollectAssignedPathsFromExpr(logical.Left, paths);
                CollectAssignedPathsFromExpr(logical.Right, paths);
                break;

            case Expr.Unary unary:
                CollectAssignedPathsFromExpr(unary.Right, paths);
                break;

            case Expr.Call call:
                CollectAssignedPathsFromExpr(call.Callee, paths);
                foreach (var arg in call.Arguments)
                    CollectAssignedPathsFromExpr(arg, paths);
                break;

            case Expr.Get get:
                CollectAssignedPathsFromExpr(get.Object, paths);
                break;

            case Expr.GetIndex getIndex:
                CollectAssignedPathsFromExpr(getIndex.Object, paths);
                CollectAssignedPathsFromExpr(getIndex.Index, paths);
                break;

            case Expr.Ternary ternary:
                CollectAssignedPathsFromExpr(ternary.Condition, paths);
                CollectAssignedPathsFromExpr(ternary.ThenBranch, paths);
                CollectAssignedPathsFromExpr(ternary.ElseBranch, paths);
                break;

            case Expr.ArrayLiteral array:
                foreach (var element in array.Elements)
                    CollectAssignedPathsFromExpr(element, paths);
                break;

            case Expr.ObjectLiteral obj:
                foreach (var prop in obj.Properties)
                    CollectAssignedPathsFromExpr(prop.Value, paths);
                break;

            case Expr.NullishCoalescing nullish:
                CollectAssignedPathsFromExpr(nullish.Left, paths);
                CollectAssignedPathsFromExpr(nullish.Right, paths);
                break;

            case Expr.Grouping grouping:
                CollectAssignedPathsFromExpr(grouping.Expression, paths);
                break;

            case Expr.NonNullAssertion nonNull:
                CollectAssignedPathsFromExpr(nonNull.Expression, paths);
                break;

            case Expr.ArrowFunction:
                // Don't descend into arrow functions - they have their own scope
                // Assignments inside don't affect outer narrowings during the loop's execution
                break;

            case Expr.New newExpr:
                foreach (var arg in newExpr.Arguments)
                    CollectAssignedPathsFromExpr(arg, paths);
                break;

            case Expr.TemplateLiteral template:
                foreach (var exprPart in template.Expressions)
                    CollectAssignedPathsFromExpr(exprPart, paths);
                break;

            case Expr.TypeAssertion typeAssertion:
                CollectAssignedPathsFromExpr(typeAssertion.Expression, paths);
                break;

            case Expr.Await awaitExpr:
                CollectAssignedPathsFromExpr(awaitExpr.Expression, paths);
                break;

            case Expr.Spread spread:
                CollectAssignedPathsFromExpr(spread.Expression, paths);
                break;

            // Literals, variables, this, etc. don't contain assignments
        }
    }
}
