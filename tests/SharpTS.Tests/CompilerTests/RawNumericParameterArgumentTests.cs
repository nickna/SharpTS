using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

[Collection("CompilationService")]
public sealed class RawNumericParameterArgumentTests
{
    [Fact]
    public void ProvenNumericArgumentsKeepNativeSignature()
    {
        const string source = "function update(input:number):number{return input+1}console.log(update(7));";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var compiler = new ILCompiler($"numeric_argument_3324_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, new DeadCodeAnalyzer(typeMap).Analyze(statements));
        var assembly = Assembly.Load(compiler.SaveToBytes());
        var method = assembly.GetType("$Program")!.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(candidate => candidate.Name.EndsWith("update", StringComparison.Ordinal));
        Assert.Equal(typeof(double), method.GetParameters().Single().ParameterType);
        Assert.Equal(typeof(double), method.ReturnType);
        Assert.Equal(8d, method.Invoke(null, new object[] { 7d }));
    }
}
