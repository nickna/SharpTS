using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class NamespaceForwardConstructorAnnotationTests
{
    [Theory]
    [InlineData("namespace N", "")]
    [InlineData("namespace Outer { export namespace N", "}")]
    public void EarlierClassBodyCanUseLaterNamespaceConstructor(string prefix, string ending)
    {
        string source = prefix + " { export class C { static test(other: typeof D): number { return other.answer; } } " +
            "export class D extends C { static answer: number = 1; } }" + ending;
        new TypeChecker().Check(Parse(source));
        Assert.Empty(new TypeChecker().CheckWithRecovery(Parse(source)).Diagnostics);
    }

    [Fact]
    public void ForwardConstructorIncludesMembersInheritedFromTheEarlierOwner()
    {
        Assert.Empty(new TypeChecker().CheckWithRecovery(Parse("""
            namespace N {
                export class C {
                    static test(other: typeof D): number { return other.answer; }
                    static answer: number = 1;
                }
                export class D extends C {}
            }
            """)).Diagnostics);
    }

    [Fact]
    public void ReopenedAndSiblingNamespacesRetainTheirOwnForwardDeclarations()
    {
        Assert.Empty(new TypeChecker().CheckWithRecovery(Parse("""
            class Later { static answer: boolean = true; }
            namespace Left { export class Base {} }
            namespace Left {
                export class Reader { static read(other: typeof Later): number { return other.answer; } }
                export class Later extends Base { static answer: number = 1; }
            }
            namespace Right {
                export class Reader { static read(other: typeof Later): string { return other.answer; } }
                export class Later { static answer: string = 'right'; }
            }
            """)).Diagnostics);
    }

    [Theory]
    [InlineData("namespace N { class C { static read(other: typeof D) { return other.missing; } } class D {} }", "TS2339")]
    [InlineData("namespace N { class C { static read(other: typeof D): string { return other.answer; } } class D { static answer: number = 1; } }", "TS2322")]
    [InlineData("namespace N { const value = new Later(); class Later {} }", "TS2449")]
    [InlineData("namespace N { class Earlier extends Later {} class Later {} }", "TS2449")]
    public void ForwardTypeVisibilityPreservesMemberAndValueOrderErrors(string source, string code)
        => Assert.Equal(code, Assert.Single(new TypeChecker().CheckWithRecovery(Parse(source)).Diagnostics).TsCode);

    [Fact]
    public void ImportedNamespaceKeepsForwardConstructorTypes()
    {
        string entry = Path.GetFullPath("namespace-forward-constructor/main.ts");
        var files = new Dictionary<string, string>
        {
            [Path.Combine(Path.GetDirectoryName(entry)!, "model.ts")] = """
                export namespace N {
                    export class Reader { static read(other: typeof Later): number { return other.answer; } }
                    export class Later { static answer: number = 1; }
                }
                """,
            [entry] = "import { N } from './model'; const value: number = N.Reader.read(N.Later);",
        };
        var resolver = new ModuleResolver(entry, files, TypeScriptProgramOptions.Disabled);
        var program = resolver.LoadProgram(entry);
        var checker = new TypeChecker();
        checker.CheckModules(resolver.GetModulesInOrder(program), resolver);
        Assert.Empty(checker.GetDiagnostics());
    }

    private static List<Stmt> Parse(string source) => new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
}
