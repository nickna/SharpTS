using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class DataViewBufferTypeTests
{
    [Theory]
    [InlineData("const view = new DataView(new ArrayBuffer(8)); const n: number = view.getUint8(0);")]
    [InlineData("const view = new DataView(new SharedArrayBuffer(8), 1, 2); const n: number = view.byteLength;")]
    [InlineData("function f() { class DataView { constructor(public label: string) {} } const s: string = new DataView('local').label; }")]
    public void LoadedAndLocalConstructorsUseTheirDeclaredSignatures(string source)
    {
        Assert.Empty(CheckWithLibraries(source));
    }

    [Theory]
    [InlineData("new DataView(1);", "TS2769")]
    [InlineData("new DataView(new ArrayBuffer(8), 'offset');", "TS2769")]
    [InlineData("new DataView();", "TS2769")]
    [InlineData("const n: string = new DataView(new ArrayBuffer(8)).byteLength;", "TS2322")]
    [InlineData("function f() { class DataView { constructor(label: string) {} } new DataView(1); }", "TS2345")]
    public void DeclaredConstructorArgumentsAndResultsRemainChecked(string source, string code)
    {
        Assert.Contains(CheckWithLibraries(source), diagnostic => diagnostic.TsCode == code);
    }

    private static IEnumerable<SharpTS.Diagnostics.Diagnostic> CheckWithLibraries(string source)
    {
        string path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "dataview-declared-buffer", "main.ts"));
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            TypeScriptProgramOptions.Default with { Lib = ["lib.es2022.d.ts", "lib.dom.d.ts"] });
        var entry = resolver.LoadProgram(path);
        var checker = new TypeChecker(maxErrors: 20);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }

    [Theory]
    [InlineData("ArrayBuffer", false, true)]
    [InlineData("ArrayBuffer", true, true)]
    [InlineData("SharedArrayBuffer", false, true)]
    [InlineData("SharedArrayBuffer", true, true)]
    [InlineData("ArrayBuffer", false, false)]
    [InlineData("ArrayBuffer", true, false)]
    [InlineData("SharedArrayBuffer", false, false)]
    [InlineData("SharedArrayBuffer", true, false)]
    public void RejectsUnrelatedNamespacedBufferInterfaces(string name, bool insideNamespace, bool declareGlobal)
    {
        string body = $"function use(buffer: {(insideNamespace ? name : "NS." + name)}){{new DataView(buffer);}}";
        string source = $$"""
            {{(declareGlobal ? "interface ArrayBuffer { readonly byteLength: number; } interface SharedArrayBuffer { readonly byteLength: number; }" : "")}}
            namespace NS {
                export interface {{name}} { marker: number; }
                {{(insideNamespace ? body : "")}}
            }
            {{(insideNamespace ? "" : body)}}
            """;
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var error = Assert.ThrowsAny<TypeCheckException>(() => new TypeChecker().Check(statements));
        Assert.Contains("DataView buffer must be an ArrayBuffer or SharedArrayBuffer", error.Message);
    }

    [Theory]
    [InlineData("ArrayBuffer")]
    [InlineData("SharedArrayBuffer")]
    public void AcceptsInterfacesAssignableToDeclaredGlobalBuffers(string name)
    {
        string source = $$"""
            interface {{name}} { readonly byteLength: number; }
            interface ValidBuffer extends {{name}} { marker: number; }
            function use(buffer: ValidBuffer) { new DataView(buffer); }
            """;
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        new TypeChecker().Check(statements);
    }
}
