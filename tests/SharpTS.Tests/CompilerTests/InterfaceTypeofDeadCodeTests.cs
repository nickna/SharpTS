using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class InterfaceTypeofDeadCodeTests
{
    [Theory]
    [InlineData("interface Box{field:string;}function read(value:Box){if(typeof value==='object'){1;}else{2;}}")]
    [InlineData("interface Box{field:string;}function read(value:Box|number){if(typeof value==='object'){1;}else{2;}}")]
    [InlineData("interface Box<T>{field:T;}function read(value:Box<string>|number){if(typeof value==='object'){1;}else{2;}}")]
    [InlineData("interface Fn{<T>(value:T):T;}function read(value:Fn|number){if(typeof value==='function'){1;}else{2;}}")]
    [InlineData("function read(value:(<T>(value:T)=>T)|number){if(typeof value==='function'){1;}else{2;}}")]
    [InlineData("interface Box{field:string;}function read<T extends Box|number>(value:T){if(typeof value==='object'){1;}else{2;}}")]
    [InlineData("function read(value:{field:string}){if(typeof value==='object'){1;}else{2;}}")]
    public void StructuralAndGenericContractsKeepBothEmittedBranches(string source)
    {
        var statements = TestHarness.ParseOrThrow(source);
        var result = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements);
        Assert.Empty(result.Diagnostics);
        var function = Assert.IsType<Stmt.Function>(statements[^1]);
        var conditional = Assert.IsType<Stmt.If>(Assert.Single(function.Body!));
        var deadCode = new DeadCodeAnalyzer(result.TypeMap).Analyze(statements);
        Assert.Equal(IfBranchResult.BothReachable, deadCode.GetIfResult(conditional));
        Assert.False(deadCode.IsDead(conditional.ThenBranch));
        Assert.False(deadCode.IsDead(conditional.ElseBranch!));
    }

    [Theory]
    [InlineData("number", "number", IfBranchResult.OnlyThenReachable)]
    [InlineData("string", "number", IfBranchResult.OnlyElseReachable)]
    [InlineData("null", "object", IfBranchResult.OnlyThenReachable)]
    [InlineData("undefined", "undefined", IfBranchResult.OnlyThenReachable)]
    [InlineData("string|number", "object", IfBranchResult.OnlyElseReachable)]
    public void ExactPrimitiveDomainsRetainTheirOptimizations(string annotation, string tag, IfBranchResult expected)
    {
        var statements = TestHarness.ParseOrThrow($"function read(value:{annotation}){{if(typeof value==='{tag}'){{1;}}else{{2;}}}}");
        var result = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements);
        Assert.Empty(result.Diagnostics);
        var conditional = Assert.IsType<Stmt.If>(Assert.Single(Assert.IsType<Stmt.Function>(statements[0]).Body!));
        Assert.Equal(expected, new DeadCodeAnalyzer(result.TypeMap).Analyze(statements).GetIfResult(conditional));
    }
}
