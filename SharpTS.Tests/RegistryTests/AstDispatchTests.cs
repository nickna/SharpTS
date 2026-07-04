using System.Runtime.CompilerServices;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using Xunit;

namespace SharpTS.Tests.RegistryTests;

/// <summary>
/// Guards the convergence of the AST dispatch tables onto <see cref="AstNodeCatalog"/> (issue #1243,
/// epic #1094). The catalog is the single source of truth for the AST node set; these tests ensure
/// the hand-maintained <see cref="AstVisitorBase"/> dispatch switch cannot silently omit a node the
/// catalog knows about. <see cref="NodeRegistry{TContext, TExprResult, TStmtResult}"/>'s own
/// exhaustiveness (interpreter + type-checker handlers) is already enforced at startup via the same
/// catalog, so its static initializer running under any test transitively covers that table.
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
    public void Probe_detects_an_unhandled_node()
    {
        // Sanity check on the discriminator itself: an out-of-catalog node DOES hit the default arm.
        Assert.True(HitsDefaultArm(() => new ProbeVisitor().Visit(new UnknownExpr())));
        Assert.True(HitsDefaultArm(() => new ProbeVisitor().Visit(new UnknownStmt())));
    }

    /// <summary>
    /// Runs <paramref name="visit"/> against an uninitialized node and reports whether dispatch fell
    /// through to <see cref="AstVisitorBase"/>'s default arm. That arm is the ONLY source of
    /// <see cref="NotSupportedException"/> in the visitor: a handled node either returns cleanly (a
    /// leaf) or throws <see cref="NullReferenceException"/> as its traversal walks the uninitialized
    /// node's null children — neither of which is a NotSupportedException.
    /// </summary>
    private static bool HitsDefaultArm(Action visit)
    {
        try
        {
            visit();
            return false;
        }
        catch (NotSupportedException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }
}
