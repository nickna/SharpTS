using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class DataViewBufferTypeTests
{
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
