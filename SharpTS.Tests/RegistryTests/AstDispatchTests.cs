using System.Runtime.CompilerServices;
using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.RegistryTests;

/// <summary>
/// Guards the convergence of the AST dispatch tables onto <see cref="AstNodeCatalog"/> (issue #1243,
/// epic #1094). The catalog is the single source of truth for the AST node set; these tests ensure
/// none of the hand-maintained dispatch switches — <see cref="AstVisitorBase"/>, the interpreter's
/// (<c>Interpreter.Dispatch.cs</c>), and the type checker's (<c>TypeChecker.Dispatch.cs</c>) — can
/// silently omit a node the catalog knows about. The latter two replaced the reflection-built
/// NodeRegistry tables (#1324): registration is now checked by the compiler (a missing Visit*
/// method is a build error) and exhaustiveness by these probes.
/// </summary>
public class AstDispatchTests
{
    /// <summary>A concrete visitor that keeps AstVisitorBase's default child-traversal behavior.</summary>
    private sealed class ProbeVisitor : AstVisitorBase;

    // Expr/Stmt subtypes deliberately absent from the catalog, used to prove the dispatch switch's
    // default arm actually throws for an unhandled node — i.e. that the probe below distinguishes
    // "handled" from "unhandled" rather than passing vacuously.
    private sealed record UnknownExpr : Expr;
    private sealed record UnknownStmt : Stmt;

    [Fact]
    public void Catalog_is_populated_and_excludes_non_node_records()
    {
        Assert.NotEmpty(AstNodeCatalog.ExprTypes);
        Assert.NotEmpty(AstNodeCatalog.StmtTypes);

        // Representative nodes are present.
        Assert.Contains(typeof(Expr.Binary), AstNodeCatalog.ExprTypes);
        Assert.Contains(typeof(Stmt.If), AstNodeCatalog.StmtTypes);

        // Every entry is a concrete subtype of its base.
        Assert.All(AstNodeCatalog.ExprTypes, t =>
        {
            Assert.True(typeof(Expr).IsAssignableFrom(t));
            Assert.False(t.IsAbstract);
        });
        Assert.All(AstNodeCatalog.StmtTypes, t =>
        {
            Assert.True(typeof(Stmt).IsAssignableFrom(t));
            Assert.False(t.IsAbstract);
        });

        // Helper records nested under the bases that are not dispatched nodes stay out.
        Assert.DoesNotContain(typeof(Expr.Property), AstNodeCatalog.ExprTypes);
        Assert.DoesNotContain(typeof(Stmt.Parameter), AstNodeCatalog.StmtTypes);
    }

    [Fact]
    public void AstVisitorBase_dispatches_every_Expr_node()
    {
        foreach (var type in AstNodeCatalog.ExprTypes)
        {
            var node = (Expr)RuntimeHelpers.GetUninitializedObject(type);
            Assert.False(
                HitsDefaultArm(() => new ProbeVisitor().Visit(node)),
                $"AstVisitorBase.Visit(Expr) has no dispatch case for Expr.{type.Name}; " +
                $"add a case that calls a Visit{type.Name} method.");
        }
    }

    [Fact]
    public void AstVisitorBase_dispatches_every_Stmt_node()
    {
        foreach (var type in AstNodeCatalog.StmtTypes)
        {
            var node = (Stmt)RuntimeHelpers.GetUninitializedObject(type);
            Assert.False(
                HitsDefaultArm(() => new ProbeVisitor().Visit(node)),
                $"AstVisitorBase.Visit(Stmt) has no dispatch case for Stmt.{type.Name}; " +
                $"add a case that calls a Visit{type.Name} method.");
        }
    }

    [Fact]
    public void Interpreter_dispatches_every_Expr_node_sync_and_async()
    {
        using var interpreter = new Interpreter(TextWriter.Null, TextWriter.Null);
        foreach (var type in AstNodeCatalog.ExprTypes)
        {
            var node = (Expr)RuntimeHelpers.GetUninitializedObject(type);
            Assert.False(
                HitsDefaultArm(() => interpreter.DispatchExpr(node)),
                $"Interpreter.DispatchExpr has no case for Expr.{type.Name}.");
            // Async exhaustiveness is deliberate (#930): every Expr must have an explicit async
            // arm, not a silent sync fallback. The returned ValueTask is not awaited — reaching
            // a handler at all (vs the default arm's synchronous throw) is the assertion.
            Assert.False(
                HitsDefaultArm(() => _ = interpreter.DispatchExprAsync(node)),
                $"Interpreter.DispatchExprAsync has no case for Expr.{type.Name}.");
        }
    }

    [Fact]
    public void Interpreter_dispatches_every_Stmt_node()
    {
        using var interpreter = new Interpreter(TextWriter.Null, TextWriter.Null);
        foreach (var type in AstNodeCatalog.StmtTypes)
        {
            var node = (Stmt)RuntimeHelpers.GetUninitializedObject(type);
            Assert.False(
                HitsDefaultArm(() => interpreter.DispatchStmt(node)),
                $"Interpreter.DispatchStmt has no case for Stmt.{type.Name}.");
        }
    }

    [Fact]
    public void TypeChecker_dispatches_every_Expr_and_Stmt_node()
    {
        var checker = new TypeChecker();
        foreach (var type in AstNodeCatalog.ExprTypes)
        {
            var node = (Expr)RuntimeHelpers.GetUninitializedObject(type);
            Assert.False(
                HitsDefaultArm(() => checker.DispatchExpr(node)),
                $"TypeChecker.DispatchExpr has no case for Expr.{type.Name}.");
        }
        foreach (var type in AstNodeCatalog.StmtTypes)
        {
            var node = (Stmt)RuntimeHelpers.GetUninitializedObject(type);
            Assert.False(
                HitsDefaultArm(() => checker.DispatchStmt(node)),
                $"TypeChecker.DispatchStmt has no case for Stmt.{type.Name}.");
        }
    }

    [Fact]
    public void Probe_detects_an_unhandled_node()
    {
        // Sanity check on the discriminator itself: an out-of-catalog node DOES hit the default arm.
        Assert.True(HitsDefaultArm(() => new ProbeVisitor().Visit(new UnknownExpr())));
        Assert.True(HitsDefaultArm(() => new ProbeVisitor().Visit(new UnknownStmt())));

        using var interpreter = new Interpreter(TextWriter.Null, TextWriter.Null);
        Assert.True(HitsDefaultArm(() => interpreter.DispatchExpr(new UnknownExpr())));
        Assert.True(HitsDefaultArm(() => _ = interpreter.DispatchExprAsync(new UnknownExpr())));
        Assert.True(HitsDefaultArm(() => interpreter.DispatchStmt(new UnknownStmt())));

        var checker = new TypeChecker();
        Assert.True(HitsDefaultArm(() => checker.DispatchExpr(new UnknownExpr())));
        Assert.True(HitsDefaultArm(() => checker.DispatchStmt(new UnknownStmt())));
    }

    /// <summary>
    /// Runs <paramref name="visit"/> against an uninitialized node and reports whether dispatch fell
    /// through to a switch's default arm. Every guarded switch's default arm throws
    /// <see cref="NotSupportedException"/> with a message containing "no dispatch case" — a marker no
    /// handler body produces. A handled node either returns cleanly (a leaf) or throws something
    /// else (typically <see cref="NullReferenceException"/> from the uninitialized node's null
    /// children); either way the dispatch arm itself was reached.
    /// </summary>
    private static bool HitsDefaultArm(Action visit)
    {
        try
        {
            visit();
            return false;
        }
        catch (NotSupportedException e) when (e.Message.Contains("no dispatch case"))
        {
            return true;
        }
        catch
        {
            return false;
        }
    }
}
