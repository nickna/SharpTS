using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.ParserTests;

public sealed class LeadingAsyncExpressionTests
{
    public const string OriginalVmSource = "import * as vm from 'node:vm';async function main(){const ctx:any=vm.createContext({});const C:any=vm.runInContext('Promise',ctx);const f:any=vm.runInContext('async()=>{await 0;return Promise}',ctx);const outer:any=Promise;console.log(await f()===C,Promise===outer);}main();";

    [Theory]
    [InlineData("async()=>{await 0;return Promise}")]
    [InlineData("async()=>await 0;")]
    [InlineData("async (value:number)=>await value;")]
    [InlineData("async value=>await value;")]
    [InlineData("async<T>(value:T)=>value;")]
    [InlineData("async/**/()=>0;")]
    [InlineData("{async()=>0;}")]
    [InlineData("namespace Box {async()=>0;}")]
    public void AsyncArrowExpressionStatementsKeepTheirActualGrammar(string source)
    {
        var statement = Assert.Single(TestHarness.ParseOrThrow(source));
        if (statement is Stmt.Block block) statement = Assert.Single(block.Statements);
        if (statement is Stmt.Namespace ns) statement = Assert.Single(ns.Members);
        Assert.True(Assert.IsType<Expr.ArrowFunction>(Assert.IsType<Stmt.Expression>(statement).Expr).IsAsync);
    }

    [Fact]
    public void OriginalVmSourceParsesItsExactUnparenthesizedScript()
    {
        var main = Assert.Single(TestHarness.ParseOrThrow(OriginalVmSource).OfType<Stmt.Function>());
        var callback = main.Body!.OfType<Stmt.Const>().Single(value => value.Name.Lexeme == "f");
        string script = Assert.IsType<string>(Assert.IsType<Expr.Literal>(Assert.IsType<Expr.Call>(callback.Initializer).Arguments[0]).Value);
        Assert.Equal("async()=>{await 0;return Promise}", script);
        AsyncArrowExpressionStatementsKeepTheirActualGrammar(script);
        // The returned Promise's creation-realm behavior remains the separate #3227 contract.
    }

    [Fact]
    public void AsyncAsASynchronousArrowParameterKeepsItsMeaning()
    {
        var arrow = Assert.IsType<Expr.ArrowFunction>(Assert.IsType<Stmt.Expression>(Assert.Single(TestHarness.ParseOrThrow("async=>async;"))).Expr);
        Assert.False(arrow.IsAsync);
        Assert.Equal("async", Assert.Single(arrow.Parameters).Name.Lexeme);
    }

    [Theory]
    [InlineData("const async=(value:number)=>value;async(1);")]
    [InlineData("const async=<T>(value:T)=>value;async<number>(1);")]
    public void FailedArrowSpeculationRetainsTheContextualIdentifierCall(string source)
    {
        var tokens = new Lexer(source).ScanTokens();
        var original = tokens.ToArray();
        var parsed = new Parser(tokens).ParseOrThrow();
        var call = Assert.IsType<Expr.Call>(Assert.IsType<Stmt.Expression>(parsed[^1]).Expr);
        Assert.Equal("async", Assert.IsType<Expr.Variable>(call.Callee).Name.Lexeme);
        Assert.Equal(original.Length, tokens.Count);
        for (int index = 0; index < tokens.Count; index++) Assert.Same(original[index], tokens[index]);
    }

    [Theory]
    [InlineData("async function run(){}", false)]
    [InlineData("async function* run(){yield 0;}", true)]
    [InlineData("export async function run(){}", false)]
    public void ActualAsyncFunctionDeclarationsRetainTheirFlags(string source, bool generator)
    {
        var statement = Assert.Single(TestHarness.ParseOrThrow(source));
        if (statement is Stmt.Export export) statement = export.Declaration!;
        var function = Assert.IsType<Stmt.Function>(statement);
        Assert.True(function.IsAsync);
        Assert.Equal(generator, function.IsGenerator);
    }

    [Fact]
    public void NewlineBeforeFunctionDoesNotCreateAnAsyncDeclaration()
    {
        var statements = TestHarness.ParseOrThrow("let async=1;async\nfunction run(){}");
        Assert.IsType<Expr.Variable>(Assert.IsType<Stmt.Expression>(statements[1]).Expr);
        Assert.False(Assert.IsType<Stmt.Function>(statements[2]).IsAsync);
    }

    [Theory]
    [InlineData("async()\n=>0;")]
    [InlineData("async():Promise<number>\n=>0;")]
    [InlineData("async()=>;")]
    [InlineData("async value=>")]
    [InlineData("async (value)=>")]
    [InlineData("async\n()=>0;")]
    [InlineData("(async\n()=>0);")]
    [InlineData("async/*\n*/()=>0;")]
    [InlineData("async function (){}")]
    [InlineData("async function* (){}")]
    public void InvalidAsyncSyntaxIsStillRejected(string source)
        => Assert.False(new Parser(new Lexer(source).ScanTokens()).Parse().IsSuccess);

    [Fact]
    public void EditorHeaderKeepsTheWrittenAsyncPrefixAndOffsets()
    {
        const string source = "async (value:number)=>await value;";
        var artifact = Parser.ParseForEditor(new SourceDocument("leading-async.ts", source), source.Length,
            EditorQueryKind.Syntax, EditorRecoveryPolicy.Default);
        Assert.True(artifact.ParseResult.IsSuccess);
        var arrow = Assert.IsType<Expr.ArrowFunction>(Assert.IsType<Stmt.Expression>(Assert.Single(artifact.ParseResult.Statements)).Expr);
        Assert.True(arrow.IsAsync);
        Assert.Contains(artifact.Syntax.GetRecords(arrow), record => record.Role == EditorSyntaxRole.Header &&
            record.IsAuthoritative && source[record.Span.Start..record.Span.End] == "async (value:number)=>");
    }
}