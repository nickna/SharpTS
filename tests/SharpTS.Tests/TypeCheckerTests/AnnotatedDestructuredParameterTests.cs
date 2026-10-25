using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using BindingFlags = System.Reflection.BindingFlags;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class AnnotatedDestructuredParameterTests
{
    private const string ActionType = "type Action={kind:'move';layerId:number}|{kind:'other'}; ";

    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, codes) in new (string, string[])[]
        {
            ("function read({action}:{action:Action}){if(action.kind==='move'){const value:number=action.layerId;}}", []),
            ("const read=({action}:{action:Action})=>{if(action.kind==='move'){const value:number=action.layerId;}};", []),
            ("const read=({action}:{action:Action})=>action.kind==='move'?action.layerId:0;", []),
            ("const read=function({action}:{action:Action}){if(action.kind==='move'){const value:number=action.layerId;}};", []),
            ("function read({action:current}:{action:Action}){if(current.kind==='move'){const value:number=current.layerId;}}", []),
            ("function read({payload:{action}}:{payload:{action:Action}}){if(action.kind==='move'){const value:number=action.layerId;}}", []),
            ("function read([action]:[Action]){if(action.kind==='move'){const value:number=action.layerId;}}", []),
            ("const read=([{action}]:[{action:Action}])=>{if(action.kind==='move'){const value:number=action.layerId;}};", []),
            ("function read({action}:{action:Action}){if(action.kind==='move'){return ()=>action.layerId;}}", []),
            ("class Reader{read({action}:{action:Action}){if(action.kind==='move'){const value:number=action.layerId;}}}", []),
            ("function read({value}:{value:'left'|'right'}){const before:'left'|'right'=value;value='right';}", []),
            ("function read({action}:{action:Action}){const value:number=action.layerId;}", ["TS2339"]),
            ("const read=({action}:{action:Action})=>action.layerId;", ["TS2339"]),
            ("function read({action}:{action:Action}){if(action.kind==='move'){action={kind:'other'};const value:number=action.layerId;}}", ["TS2339"]),
            ("const read=({action}:{action:Action})=>{if(action.kind==='move'){action={kind:'other'};const value:number=action.layerId;}};", ["TS2339"]),
            ("function read({action}:{action:Action}){action.kind='unknown';}", ["TS2322"]),
            ("function read({action}:{action:Action}){if(action.kind==='move'){let action={kind:'move'};const value:number=action.layerId;}}", ["TS2339"]),
            ("function read({action}:{action:Action}){let local={kind:'move'};local.kind='other';const wrong:'move'=local.kind;}", ["TS2322"]),
            ("function read({action}:{action:Action}){if(action.kind==='move'){const later=()=>action.layerId;action={kind:'other'};}}", ["TS2339"]),
        })
            foreach (bool modules in new[] { false, true }) yield return [ActionType + source, codes, modules];
    }

    [Theory, MemberData(nameof(Cases))]
    public void LoweredAliasesRetainAnnotatedLiteralsWithoutInventingGuardOrStabilityProof(string source, string[] codes, bool modules) =>
        Assert.Equal(codes, Check(source, modules).Select(diagnostic => diagnostic.TsCode));

    [Fact]
    public void LoweringKeepsTheWrittenAnnotationAndExactAliasType()
    {
        var statements = TestHarness.ParseOrThrow(ActionType + "function read({action}:{action:Action}){action;}");
        var function = Assert.IsType<Stmt.Function>(statements[1]);
        var parameter = Assert.Single(function.Parameters);
        var annotation = parameter.TypeAnnotationNode;
        Assert.NotNull(annotation);
        var initializer = Assert.IsType<Stmt.Sequence>(function.Body![0]);
        Assert.Same(parameter.Name, initializer.ParameterName);
        var alias = Assert.Single(initializer.Statements.OfType<Stmt.Var>(), variable => variable.Name.Lexeme == "action");
        Assert.Null(alias.TypeAnnotation);
        Assert.Null(alias.TypeAnnotationNode);
        var checkedProgram = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements);
        Assert.Empty(checkedProgram.Diagnostics);
        Assert.Same(annotation, parameter.TypeAnnotationNode);
        var projected = Assert.IsType<TypeInfo.Union>(checkedProgram.TypeMap.Get(alias.Initializer!));
        Assert.Equal(new[] { "move", "other" }, projected.FlattenedTypes.Select(type =>
            Assert.IsType<TypeInfo.StringLiteral>(Assert.IsType<TypeInfo.Record>(type).Fields["kind"]).Value).Order());
    }

    [Theory]
    [InlineData("Check")]
    [InlineData("Recovery")]
    [InlineData("Modules")]
    public void ReusingCheckerReleasesOldPrologueProvenanceAndTracksOnlyCurrentDeclarations(string entry)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        var previous = TestHarness.ParseOrThrow("function first({action}:{action:{kind:'move'}}){const kind:'move'=action.kind;}");
        Assert.Empty(checker.CheckWithRecovery(previous).Diagnostics);
        var oldBindings = PrologueBindings(previous);
        var provenance = Assert.IsType<HashSet<Stmt.Var>>(typeof(TypeChecker).GetField(
            "_annotatedParameterBindings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(checker));
        Assert.All(oldBindings, variable => Assert.Contains(provenance, candidate => ReferenceEquals(candidate, variable)));

        const string source = "function second({action}:{action:{kind:'other'}}){const kind:'other'=action.kind;let local={kind:'move'};local.kind='other';}";
        List<Stmt> current;
        if (entry == "Modules")
        {
            string path = Path.Combine(Path.GetTempPath(), "reused-parameter-provenance-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
                new TypeScriptProgramOptions { NoLib = true });
            var module = resolver.LoadModule(path);
            current = module.Statements;
            checker.CheckModules(resolver.GetModulesInOrder(module), resolver);
            Assert.Empty(checker.GetDiagnostics());
        }
        else
        {
            current = TestHarness.ParseOrThrow(source);
            if (entry == "Check") checker.Check(current);
            else Assert.Empty(checker.CheckWithRecovery(current).Diagnostics);
        }

        Assert.All(oldBindings, variable => Assert.DoesNotContain(provenance, candidate => ReferenceEquals(candidate, variable)));
        var currentBindings = PrologueBindings(current);
        Assert.Equal(currentBindings.Length, provenance.Count);
        Assert.All(currentBindings, variable => Assert.Contains(provenance, candidate => ReferenceEquals(candidate, variable)));
        Assert.Empty(checker.CheckWithRecovery(TestHarness.ParseOrThrow("let local={kind:'move'};local.kind='other';")).Diagnostics);
        Assert.Empty(provenance);
    }

    private static Stmt.Var[] PrologueBindings(IEnumerable<Stmt> statements) =>
        Assert.IsType<Stmt.Sequence>(Assert.Single(statements.OfType<Stmt.Function>()).Body![0])
            .Statements.OfType<Stmt.Var>().ToArray();

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "annotated-destructured-parameter-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source },
            new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}
