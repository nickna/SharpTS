using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;
using Xunit.Abstractions;

namespace SharpTS.Tests.CompilerTests;

public class PromiseRuntimeFeatureGatingTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("console.log(1);", false)]
    [InlineData("setTimeout(() => console.log(1), 0);", false)]
    [InlineData("Promise.resolve(1);", true)]
    [InlineData("const P: any = Promise; new P(() => {});", true)]
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
        output.WriteLine($"Promise-free assembly size: {assemblyBytes.Length:N0} bytes.");
        Assembly assembly = Assembly.Load(assemblyBytes);
        Type[] types = assembly.GetTypes();

        Assert.DoesNotContain(types, type =>
            type.Name.Contains("Promise", StringComparison.Ordinal));
        Assert.DoesNotContain(types, type => type.Name == "$DOMException");
        Assert.DoesNotContain(types.SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)),
            method => method.Name.StartsWith("ArrayBinding", StringComparison.Ordinal) ||
                      method.Name == "ArrayIteratorNext" ||
                      method.Name.StartsWith("With", StringComparison.Ordinal));

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

        // Optional native ancestry checks must follow the same feature gates
        // as their carriers, including the names stored in the metadata heap.
        string[] optionalPrototypeCarrierNames =
        [
            "SharpTS.Runtime.Types.SharpTSIntlFormatterBase", "$ReadableStream",
            "$Request", "$AsyncLocalStorage", "$BlockList", "$CryptoKey", "$Dir"
        ];
        using var stream = new MemoryStream(assemblyBytes, writable: false);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        for (var handle = MetadataTokens.UserStringHandle(1); !handle.IsNil; handle = metadata.GetNextHandle(handle))
            Assert.DoesNotContain(metadata.GetUserString(handle), optionalPrototypeCarrierNames);

        // Keep the fixed PE size ceiling while gating optional native fallbacks
        // and class-only bodies, including generic static owner normalization.
        // Platform layout differences must fit within the existing headroom;
        // the structural absence guards remain strict.
        Assert.True(assemblyBytes.Length <= 291_840,
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
