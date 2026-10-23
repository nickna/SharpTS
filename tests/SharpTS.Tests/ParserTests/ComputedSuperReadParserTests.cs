using System.Reflection;
using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.Tests.Infrastructure;
using SharpTS.Tests.SharedTests;
using Xunit;

namespace SharpTS.Tests.ParserTests;

[Collection("CompilationService")]
public sealed class ComputedSuperReadParserTests
{
    [Fact]
    public void ComputedKeyRemainsAVisitedAndResolvedLocal()
    {
        var source = (string)ComputedSuperReadTests.Cases()
            .Single(row => (string)row[0] == "dynamic-super-index")[1];
        var statements = TestHarness.ParseOrThrow(source);
        var derived = Assert.IsType<Stmt.Class>(statements[1]);
        var method = Assert.Single(derived.Methods);
        var result = Assert.IsType<Stmt.Return>(Assert.Single(method.Body!));
        var index = Assert.IsType<Expr.GetIndex>(result.Value);
        var home = Assert.IsType<Expr.Super>(index.Object);
        var key = Assert.IsType<Expr.Variable>(index.Index);
        Assert.Equal("super", home.Keyword.Lexeme);
        Assert.Null(home.Method);
        Assert.Equal("key", key.Name.Lexeme);

        var visitor = new KeyVisitor();
        visitor.Visit(index);
        Assert.Same(home, Assert.Single(visitor.Homes));
        Assert.Same(key, Assert.Single(visitor.Keys));

        using var interpreter = new Interpreter();
        new VariableResolver(interpreter).Resolve(statements);
        var locals = Assert.IsType<Dictionary<Expr, int>>(
            typeof(Interpreter).GetField("_locals", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(interpreter));
        Assert.True(locals.TryGetValue(key, out int depth));
        Assert.Equal(0, depth);
    }

    private sealed class KeyVisitor : AstVisitorBase
    {
        public List<Expr.Variable> Keys { get; } = [];
        public List<Expr.Super> Homes { get; } = [];
        protected override void VisitVariable(Expr.Variable expr) => Keys.Add(expr);
        protected override void VisitSuper(Expr.Super expr) => Homes.Add(expr);
    }
}
