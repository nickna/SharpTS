using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ConstArrowBindingAnalyzerTests
{
    [Theory]
    [InlineData("new (class { run(callback: (x: number) => number): void { [1].map(callback); } })();")]
    [InlineData("export = class { run(callback: (x: number) => number): void { [1].map(callback); } };")]
    [InlineData("@decorate((callback: (x: number) => number) => [1].map(callback)) class Holder {}")]
    [InlineData("class Holder { @decorate((callback: (x: number) => number) => [1].map(callback)) value: number = 1; }")]
    [InlineData("class Holder { @decorate((callback: (x: number) => number) => [1].map(callback)) accessor value: number = 1; }")]
    public void ConstructorAndExportAssignmentSubtreesCannotHideBindingOwners(string consumer)
    {
        var statements = new Parser(new Lexer(
            "const callback = (x: number): number => x + 1; " + consumer).ScanTokens(),
            DecoratorMode.Stage3).ParseOrThrow();
        Assert.Empty(ConstArrowBindingAnalyzer.Collect(statements));
    }

    [Fact]
    public void SelectionProtectsIdentityAndRejectsConsumerMutation()
    {
        var statements = new Parser(new Lexer("const callback = (x: number): number => x + 1;").ScanTokens()).ParseOrThrow();
        var selected = ConstArrowBindingAnalyzer.Collect(statements);
        Assert.Same(((Stmt.Const)statements[0]).Initializer, selected["callback"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, Expr.ArrowFunction>)selected).Clear());
        statements.Clear();
        Assert.Single(selected);
        Assert.Empty(ConstArrowBindingAnalyzer.Collect([]));
    }

    [Fact]
    public void UnambiguousTypedCallbackKeepsItsDirectAdapter()
    {
        var (assembly, output) = TestHarness.CompileAndRun("""
            const callback = (x: number): number => x + 1;
            console.log([1, 2].map(callback).join(','));
            """, DecoratorMode.None);
        Assert.Equal("2,3\n", output);
        Assert.Contains(assembly.GetType("$Program")!.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
            method => method.Name.Contains("$box1", StringComparison.Ordinal)
                || method.Name.Contains("$nbox1", StringComparison.Ordinal));
    }

    [Fact]
    public void BodyContextsShareOneAdapterForTheSameArrow()
    {
        var (assembly, output) = TestHarness.CompileAndRun("""
            const callback = (x: number): number => x + 1;
            function first(): void { console.log([1].map(callback).join(',')); }
            function second(): void { console.log([2].map(callback).join(',')); }
            first(); second();
            """, DecoratorMode.None);
        Assert.Equal("2\n3\n", output);
        Assert.Single(assembly.GetType("$Program")!.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
            method => method.Name.Contains("$box1", StringComparison.Ordinal)
                || method.Name.Contains("$nbox1", StringComparison.Ordinal));
    }
}
