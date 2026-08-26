using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class PromiseRuntimeFeatureGatingTests
{
    [Theory]
    [InlineData("console.log(1);", false)]
    [InlineData("setTimeout(() => console.log(1), 0);", false)]
    [InlineData("Promise.resolve(1);", true)]
    [InlineData("async function f(): Promise<number> { return 1; }", true)]
    [InlineData("const f = async (): Promise<number> => 1;", true)]
    [InlineData("async function* f() { yield 1; }", true)]
    [InlineData("async function f() { for await (const x of [1]) {} }", true)]
    [InlineData("async function f() { await using value = create(); }", true)]
    [InlineData("import('value');", true)]
    [InlineData("import { readFile } from 'fs/promises';", true)]
    [InlineData("fetch('http://127.0.0.1/');", true)]
    public void DetectsPromiseRuntimeRequirement(string source, bool expected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        TypeMap? typeMap = statements.Any(statement => statement is Stmt.Import) ||
                           source.Contains("await using", StringComparison.Ordinal)
            ? null
            : new TypeChecker().Check(statements);

        var features = new RuntimeFeatureDetector().Detect(statements, typeMap);

        Assert.Equal(expected, features.UsesPromise);
    }

    [Fact]
    public void SynchronousProgramOmitsPromiseRuntimeAndStaysBelowSizeRatchet()
    {
        byte[] assemblyBytes = Compile("console.log(1);");
        Assembly assembly = Assembly.Load(assemblyBytes);
        Type[] types = assembly.GetTypes();

        Assert.DoesNotContain(types, type =>
            type.Name.Contains("Promise", StringComparison.Ordinal));

        string[] promiseOnlyMethods = types
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Static | BindingFlags.Instance))
            .Select(method => method.Name)
            .Where(name => name.Contains("Promise", StringComparison.Ordinal) &&
                           name != "QueuePromiseJob")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(promiseOnlyMethods.Length == 0,
            $"Promise-only methods remained: {string.Join(", ", promiseOnlyMethods)}");

        Assert.True(assemblyBytes.Length <= 275_500,
            $"Promise-free assembly grew beyond its size ratchet: {assemblyBytes.Length:N0} bytes.");
    }

    [Fact]
    public void PromiseProgramRetainsPromiseRuntime()
    {
        byte[] assemblyBytes = Compile(
            "Promise.resolve(1).then(value => console.log(value));");
        Assembly assembly = Assembly.Load(assemblyBytes);

        Assert.Contains(assembly.GetTypes(), type => type.Name == "$Promise");
        Assert.Contains(assembly.GetTypes(), type => type.Name == "$PromiseCapability");
    }

    private static byte[] Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"promise_gate_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return compiler.SaveToBytes();
    }
}
