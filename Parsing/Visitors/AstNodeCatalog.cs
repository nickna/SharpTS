using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace SharpTS.Parsing.Visitors;

/// <summary>
/// The single canonical list of concrete AST node types, derived once from the <see cref="Expr"/>
/// and <see cref="Stmt"/> record definitions in <c>Parsing/AST.cs</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the convergence point for the AST dispatch tables (issue #1243, epic #1094). Every
/// consumer that needs "the set of AST node kinds" reads it from here instead of re-deriving it:
/// <see cref="NodeRegistry{TContext, TExprResult, TStmtResult}"/> uses it for exhaustiveness
/// checks (<c>Freeze</c>/<c>FreezeAsync</c>) and handler auto-registration (<c>AutoRegister</c>),
/// and the visitor-dispatch guard test validates <see cref="AstVisitorBase"/>'s switch against it.
/// A new node added to <c>AST.cs</c> automatically appears here, so it cannot be silently omitted
/// from a dispatch table without a loud failure (a startup exception from the registry, or a
/// failing guard test for the visitor switch).
/// </para>
/// <para>
/// "Node type" is defined exactly as the dispatch tables historically defined it: a directly
/// nested type of <see cref="Expr"/>/<see cref="Stmt"/> that is concrete and inherits from the
/// base. This excludes helper records nested under the bases that are not themselves dispatched
/// nodes — <c>Expr.PropertyKey</c>/<c>Property</c>, <c>Stmt.Parameter</c>, the interface-member
/// records, and so on.
/// </para>
/// </remarks>
public static class AstNodeCatalog
{
    /// <summary>Every concrete <see cref="Expr"/> node type, in reflection order.</summary>
    public static readonly IReadOnlyList<Type> ExprTypes = CollectConcreteNodes(typeof(Expr));

    /// <summary>Every concrete <see cref="Stmt"/> node type, in reflection order.</summary>
    public static readonly IReadOnlyList<Type> StmtTypes = CollectConcreteNodes(typeof(Stmt));

    private static IReadOnlyList<Type> CollectConcreteNodes(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicNestedTypes)] Type baseType) =>
        baseType.GetNestedTypes(BindingFlags.Public)
            .Where(t => baseType.IsAssignableFrom(t) && !t.IsAbstract && t != baseType)
            .ToArray();
}
