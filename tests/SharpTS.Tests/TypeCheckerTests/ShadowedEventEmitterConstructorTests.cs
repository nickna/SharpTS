using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ShadowedEventEmitterConstructorTests
{
    [Theory]
    [InlineData("class EventEmitter { kind='local'; } const value=new EventEmitter(); value.kind;", "string")]
    [InlineData("function make(EventEmitter:new()=>{kind:string}) { const value=new EventEmitter(); value.kind; }", "string")]
    [InlineData("class EventEmitter { constructor(public kind:number){} } const Alias=EventEmitter; const value=new Alias(7); value.kind;", "number")]
    [InlineData("class EventEmitter { constructor(public kind:string, public count:number){} } const value=new EventEmitter('local',2); value.kind;", "string")]
    public void LexicalDeclarationsParametersAndAliasesKeepTheirActualInstanceShape(string source, string expected)
    {
        var document = new SourceDocument("event-emitter.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker().WithEditorMetadata(); checker.Check(statements, document);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(checker.EditorFacts.GetSourceTypeObservations(document), observation =>
            source[observation.Span.Start..observation.Span.End] == "value.kind" && observation.Display.Text == expected);
    }

    [Fact]
    public void UnboundCompatibilityConstructorStillReturnsItsNativeInstance()
    {
        const string source = "const value=new EventEmitter({captureRejections:true}); value.emit('event');";
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow(); var checker = new TypeChecker(); var map = checker.Check(statements);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var construction = ((Stmt.Const)statements[0]).Initializer!;
        Assert.IsType<TypeInfo.EventEmitter>(map.Get(construction));
    }

    [Fact]
    public void CanonicalEmbeddedImportRetainsNativeOptionsWhileLocalShadowingDoesNot()
    {
        string entry = Path.Combine(Path.GetTempPath(), "checker_event_emitter_" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(entry, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [entry] = "import {EventEmitter} from 'events'; const native=new EventEmitter({captureRejections:true}); native.emit('x'); { class EventEmitter{kind='local';} const local=new EventEmitter(); console.log(local.kind); }"
        });
        var modules = resolver.GetModulesInOrder(resolver.LoadModule(entry)); var checker = new TypeChecker(); checker.CheckModules(modules, resolver);
        Assert.DoesNotContain(checker.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
