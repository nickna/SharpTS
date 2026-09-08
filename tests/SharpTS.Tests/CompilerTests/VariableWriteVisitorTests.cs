using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class VariableWriteVisitorTests
{
    [Theory]
    [InlineData("x = 1;", "x")]
    [InlineData("x += 1;", "x")]
    [InlineData("x -= 1;", "x")]
    [InlineData("x *= 1;", "x")]
    [InlineData("x /= 1;", "x")]
    [InlineData("x %= 1;", "x")]
    [InlineData("x &= 1;", "x")]
    [InlineData("x |= 1;", "x")]
    [InlineData("x ^= 1;", "x")]
    [InlineData("x <<= 1;", "x")]
    [InlineData("x >>= 1;", "x")]
    [InlineData("x >>>= 1;", "x")]
    [InlineData("x ||= 1;", "x")]
    [InlineData("x &&= 1;", "x")]
    [InlineData("x ??= 1;", "x")]
    [InlineData("++x;", "x")]
    [InlineData("--x;", "x")]
    [InlineData("x++;", "x")]
    [InlineData("x--;", "x")]
    [InlineData("x = (y += 1);", "x,y")]
    [InlineData("[x, , ...y] = source;", "x,y")]
    [InlineData("({ field: x, ...y } = source);", "x,y")]
    [InlineData("({ field: [x = (y = 1)] } = source);", "x,y")]
    [InlineData("({ [y = 'key']: x } = source);", "x,y")]
    [InlineData("let [x, ...y] = source;", "")]
    [InlineData("const { field: x } = source;", "")]
    [InlineData("obj.x = 1; obj.x += 1; obj.x ||= 1; obj.x++; ++obj.x;", "")]
    [InlineData("obj[x] = 1; obj[x] += 1; obj[x] ??= 1; obj[x]++; ++obj[x];", "")]
    [InlineData("obj[x = 0] += (y = 1);", "x,y")]
    [InlineData("[obj.x, obj[y = 0]] = source;", "y")]
    public void DiscoversBindingWrites_AndTraversesValuesAndLoweredPatterns(string source, string expected)
    {
        var visitor = new WriteVisitor();
        foreach (var statement in Parse(source))
            visitor.Visit(statement);
        Assert.Equal(expected, string.Join(",", visitor.Writes));
    }

    [Fact]
    public void FunctionAndArrowDefaults_UseTheirOwnScope_AndRestoreTheirParent()
    {
        var visitor = new ScopeVisitor();
        foreach (var statement in Parse("""
            let value = 0;
            function outer(parameter = (value = 1)) {
                let value = 2;
                { const value = 3; }
                const arrow = (parameter = (value = 4)) => { value = 5; };
                value = 6;
            }
            value = 7;
            function sibling() { let value = 8; }
            """))
            visitor.Visit(statement);

        Assert.Equal(new[] { 1, 2, 2, 1, 0 }, visitor.Writes.Select(binding => binding.Scope));
        Assert.All(visitor.Writes, binding => Assert.Equal("value", binding.Name));
        Assert.Equal(1, visitor.DeclarationCounts[new(0, "value")]);
        Assert.Equal(2, visitor.DeclarationCounts[new(1, "value")]);
        Assert.Equal(1, visitor.DeclarationCounts[new(3, "value")]);
        Assert.DoesNotContain(visitor.DeclarationCounts.Keys, binding => binding.Name == "parameter");
    }

    [Theory]
    [InlineData("function outer() { let fail = 1; }")]
    [InlineData("const arrow = () => { let fail = 1; };")]
    [InlineData("function outer() { const arrow = () => { let fail = 1; }; }")]
    public void AbortedNestedTraversal_RestoresProgramScope(string source)
    {
        var visitor = new ScopeVisitor();
        Assert.Throws<InvalidOperationException>(() => visitor.Visit(Assert.Single(Parse(source))));
        visitor.Visit(Assert.Single(Parse("value = 1;")));
        Assert.Equal(new FunctionScopedBinding(0, "value"), Assert.Single(visitor.Writes));
    }

    private static List<Stmt> Parse(string source) =>
        new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();

    private sealed class WriteVisitor : VariableWriteVisitor
    {
        public List<string> Writes { get; } = [];
        protected override void OnVariableWrite(Token name) => Writes.Add(name.Lexeme);
    }

    private sealed class ScopeVisitor : FunctionScopedBindingVisitor
    {
        public List<FunctionScopedBinding> Writes { get; } = [];

        protected override void OnVariableWrite(Token name)
        {
            Writes.Add(Binding(name));
            base.OnVariableWrite(name);
        }

        protected override void OnDeclaration(FunctionScopedBinding binding, Token name, Expr? initializer)
        {
            if (name.Lexeme == "fail")
                throw new InvalidOperationException("Abort the traversal inside the nested scope.");
        }
    }
}
