using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class NativeClrClassFeatureGatingTests
{
    private const string Builder = """
        @DotNetType("System.Text.StringBuilder")
        declare class Builder {
            constructor();
            append(value: string): Builder;
            toString(): string;
            length: number;
        }
        """;

    [Theory]
    [InlineData("declare class Ambient {}", false)]
    [InlineData("@DotNetType('System.Text.StringBuilder') class Builder {}", false)]
    [InlineData("declare class Ambient {} class Actual {}", true)]
    [InlineData("declare class Ambient {} const Actual = class {};", true)]
    [InlineData("declare class Ambient {} class Derived extends Ambient {}", true)]
    public void NativeSelectionFollowsTheActualClassBodyProducer(string source, bool nativeClasses)
    {
        var statements = new Parser(new Lexer(source).ScanTokens(), DecoratorMode.Legacy).ParseOrThrow();
        Assert.True(new RuntimeFeatureDetector().Detect(statements).UsesUserClasses);
        Assert.Equal(nativeClasses, new RuntimeFeatureDetector(true).Detect(statements).UsesUserClasses);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectClrCallsKeepVerifiedIlWithoutUnusedNativeSourceClassHelpers(bool native)
    {
        var statements = new Parser(new Lexer(Builder + """
            const builder: Builder = new Builder();
            builder.append("native");
            console.log(builder.toString(), builder.length);
            """).ScanTokens(), DecoratorMode.Legacy).ParseOrThrow();
        var checker = new TypeChecker();
        checker.SetDecoratorMode(DecoratorMode.Legacy);
        var types = checker.Check(statements);
        var compiler = new ILCompiler("ambient_class_gate", false, true, null);
        if (native) compiler.EnableNativeAotProjectOutput();
        compiler.Compile(statements, types);
        byte[] bytes = compiler.SaveToBytes();
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        using var stream = new MemoryStream(bytes);
        Assert.Empty(verifier.Verify(stream));
        var definition = Assembly.Load(bytes).GetType("$ClassDefinition", throwOnError: true)!;
        Assert.Equal(!native, definition.GetMethod("ValidateParent") is not null);
        Assert.Equal(!native, definition.GetMethod("ReadPrototypeProperty") is not null);
    }
}
