using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using SharpTS.TypeSystem.Exceptions;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class BufferConstructorShadowTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["module-local-array-buffer", """
            export {};
            class ArrayBuffer {value=7;}
            console.log(new ArrayBuffer().value);
            """, "7\n"];
        yield return ["module-local-shared-buffer-signature", """
            export {};
            class SharedArrayBuffer {constructor(public label:string,public count:number){}}
            const value=new SharedArrayBuffer('local',7);console.log(value.label,value.count);
            """, "local 7\n"];
        yield return ["block-and-global-buffer", """
            export {};
            {class ArrayBuffer {value='local';}console.log(new ArrayBuffer().value);}
            console.log(new ArrayBuffer(4).byteLength);
            """, "local\n4\n"];
        yield return ["constructor-parameter-binding", """
            export {};
            class Local {constructor(public value:string){}}
            function make(ArrayBuffer:typeof Local){return new ArrayBuffer('parameter').value;}
            console.log(make(Local));
            """, "parameter\n"];
        yield return ["captured-constructor-binding", """
            export {};
            class Local {constructor(public value:string){}}
            function make(SharedArrayBuffer:typeof Local){return ()=>new SharedArrayBuffer('captured').value;}
            console.log(make(Local)());
            """, "captured\n"];
        yield return ["any-constructor-binding", """
            export {};
            class Local {constructor(public value:number){}}
            function make(ArrayBuffer:any){return new ArrayBuffer(6).value;}
            console.log(make(Local));
            """, "6\n"];
        yield return ["type-parameter-keeps-value-intrinsic", """
            export {};
            function make<ArrayBuffer>(){return new ArrayBuffer(4).byteLength;}
            console.log(make<number>());
            """, "4\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void SelectedBufferConstructorUsesItsOwnSignature(string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }

    [Theory, ModeData]
    public void ImportedAliasUsesItsActualConstructor(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["value.ts"] = "export class Local {constructor(public value:string){}}",
            ["main.ts"] = "import {Local as ArrayBuffer} from './value'; console.log(new ArrayBuffer('imported').value);"
        };
        Assert.Equal("imported\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory]
    [InlineData("class ArrayBuffer {} new ArrayBuffer(1);", "TS2554")]
    [InlineData("class SharedArrayBuffer {constructor(value:string){}} new SharedArrayBuffer(1);", "TS2345")]
    [InlineData("new ArrayBuffer();", "TS2554")]
    [InlineData("new SharedArrayBuffer('wrong');", "TS2345")]
    public void SourceAndIntrinsicConstructorErrorsRemainChecked(string source, string code)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var error = Assert.Throws<TypeCheckException>(() => new TypeChecker().Check(statements));
        Assert.Equal(code, error.Diagnostic.TsCode);
    }
}
