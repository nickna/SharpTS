using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class StringParameterIngressAbiTests
{
    [Fact]
    public void OrdinaryStringParameterRetainsItsClrStringSignature()
    {
        var statements = new Parser(new Lexer("function identity(value:string){return value}console.log(identity(\"safe\"));").ScanTokens()).ParseOrThrow();
        var map = new TypeChecker().Check(statements);
        var compiler = new ILCompiler($"string_ingress_abi_{Guid.NewGuid():N}");
        compiler.Compile(statements, map, new DeadCodeAnalyzer(map).Analyze(statements));
        var assembly = Assembly.Load(compiler.SaveToBytes());
        var method = assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(candidate => candidate.Name.EndsWith("identity", StringComparison.Ordinal));
        Assert.Equal(typeof(string), Assert.Single(method.GetParameters()).ParameterType);
    }

    [Fact]
    public void OrdinaryClassStringParametersRetainTheirClrStringSignatures()
    {
        var source = "class Reader{constructor(value:string){}identity(value:string){return value}}console.log(new Reader(\"ctor\").identity(\"safe\"));";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var map = new TypeChecker().Check(statements);
        var compiler = new ILCompiler($"class_string_ingress_abi_{Guid.NewGuid():N}");
        compiler.Compile(statements, map, new DeadCodeAnalyzer(map).Analyze(statements));
        var assembly = Assembly.Load(compiler.SaveToBytes());
        var type = assembly.GetType("Reader")!;
        Assert.Equal(typeof(string), Assert.Single(type.GetMethod("identity")!.GetParameters()).ParameterType);
        Assert.Contains(type.GetConstructors(), constructor =>
            constructor.GetParameters() is [{ ParameterType: var parameterType }] && parameterType == typeof(string));
    }
}
