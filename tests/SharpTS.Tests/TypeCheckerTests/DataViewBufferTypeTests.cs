using SharpTS.Parsing;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class DataViewBufferTypeTests
{
    [Theory]
    [InlineData("ArrayBuffer", false)]
    [InlineData("ArrayBuffer", true)]
    [InlineData("SharedArrayBuffer", false)]
    [InlineData("SharedArrayBuffer", true)]
    public void RejectsUnrelatedNamespacedBufferInterfaces(string name, bool insideNamespace)
    {
        string body = $"function use(buffer: {(insideNamespace ? name : "NS." + name)}){{new DataView(buffer);}}";
        string source = $$"""
            interface ArrayBuffer { readonly byteLength: number; }
            interface SharedArrayBuffer { readonly byteLength: number; }
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
