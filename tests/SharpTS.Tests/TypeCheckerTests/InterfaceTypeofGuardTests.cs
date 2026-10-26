using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class InterfaceTypeofGuardTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var (source, codes) in new (string, string[])[]
        {
            ("interface Box{field:string;}declare function take():Box|number;const value=take();if(typeof value==='object'){const wrong:number=value.field;}", ["TS2322"]),
            ("interface Box{field:string;}function read(value:Box|number){if(typeof value==='object'){const correct:string=value.field;}else{const correct:number=value;}}", []),
            ("interface Box{field:string;}function read(value:Box){if(typeof value==='object'){const wrong:number=value.field;}}", ["TS2322"]),
            ("interface Base{field:string;}interface Box extends Base{extra:boolean;}function read(value:Box|number){if(typeof value==='object'){const wrong:number=value.field;}}", ["TS2322"]),
            ("interface Box<T>{field:T;}function read(value:Box<string>|number){if(typeof value==='object'){const wrong:number=value.field;}}", ["TS2322"]),
            ("interface Box<T>{field:T;}function read(value:Box<string>|Box<number>|boolean){if(typeof value==='object'){const wrong:boolean=value.field;}}", ["TS2322"]),
            ("interface Box{field:string;}function read(holder:{value:Box|number}){if(typeof holder.value==='object'){const wrong:number=holder.value.field;}}", ["TS2322"]),
            ("interface Box{field:string;}function read(value:Box|number){if('object'===typeof value){const wrong:number=value.field;}}", ["TS2322"]),
            ("interface Box{field:string;}function read(value:Box|number){if(typeof value!=='object')return;const wrong:number=value.field;}", ["TS2322"]),
            ("interface Box{field:string;}function read(value:Box|number|null){if(typeof value==='object'&&value!==null){const wrong:number=value.field;}}", ["TS2322"]),
            ("interface Box{readonly field:string;}function write(value:Box|number){if(typeof value==='object'){value.field='changed';}}", ["TS2540"]),
            ("interface Box{field?:string;}function read(value:Box|number){if(typeof value==='object'){const wrong:string=value.field;}}", ["TS2322"]),
            ("interface Box{readonly[key:string]:string;}function write(value:Box|number){if(typeof value==='object'){value['field']='changed';}}", ["TS2542"]),
            ("interface Fn{(value:number):string;tag:string;}function read(value:Fn|number){if(typeof value==='function'){const wrong:number=value(1);}}", ["TS2322"]),
            ("interface Fn{(value:number):string;tag:string;}function read(value:Fn|number){if(typeof value==='function'){const correct:string=value(1);const tag:string=value.tag;}else{const correct:number=value;}}", []),
            ("interface Fn{(value:number):string;tag:string;}function read(value:Fn|number){if(typeof value==='object'){const unreachable:never=value;}}", []),
            ("interface Fn<T>{(value:T):T;tag:string;}function read(value:Fn<string>|number){if(typeof value==='function'){const wrong:number=value('s');}}", ["TS2322"]),
            ("interface Base{(value:number):string;tag:string;}interface Fn extends Base{extra:boolean;}function read(value:Fn|number){if(typeof value==='function'){value('wrong');}}", ["TS2345"]),
            ("interface Fn{(value:number):number;(value:string):string;tag:string;}function read(value:Fn|number){if(typeof value==='function'){const wrong:number=value('s');}}", ["TS2322"]),
            ("type Fn={(value:number):string;tag:string;};function read(value:Fn|number){if(typeof value==='function'){const wrong:number=value(1);}}", ["TS2322"]),
            ("interface Fn{<T>(value:T):T;tag:string;}function read(value:Fn|number){if(typeof value==='function'){const wrong:number=value('s');}}", ["TS2322"]),
            ("interface Ctor{new(value:number):{field:string};tag:string;}function read(value:Ctor|number){if(typeof value==='function'){const wrong:number=new value(1).field;}}", ["TS2322"]),
            ("interface Box{field:string;}function read<T extends Box>(value:T){if(typeof value==='object'){const same:T=value;const wrong:number=value.field;}}", ["TS2322"]),
            ("interface Box{field:string;}function read<T extends Box|number>(value:T){if(typeof value==='object'){const same:T=value;const correct:string=value.field;}}", []),
            ("interface Box{field:string;}function read<T extends Box|number>(value:T){if(typeof value==='object'){const wrong:number=value.field;}}", ["TS2322"]),
            ("interface Box<A>{field:A;}function read<A,T extends Box<A>|number>(value:T){if(typeof value==='object'){const same:T=value;const correct:A=value.field;}}", []),
            ("interface StringLike{length:number;}function read(value:StringLike|number){if(typeof value==='string'){const correct:string=value;const size:number=value.length;}}", []),
            ("interface StringLike{length:number;}function read(value:StringLike|number){if(typeof value==='string'){const wrong:number=value;}}", ["TS2322"]),
            ("interface Box{field:string;}function read(value:Box&{tag:boolean}|number){if(typeof value==='object'){const wrong:number=value.field;}}", ["TS2322"]),
            ("function read<T>(value:T){if(typeof value==='object'){const same:T=value;}}", []),
            ("function read<T>(value:T):T{if(typeof value==='object')return value;return value;}", []),
            ("function read<T>(value:T){if(typeof value==='object'){const wrong:string=value;}}", ["TS2322"]),
        })
            foreach (bool modules in new[] { false, true }) yield return [source, codes, modules];
    }

    [Theory, MemberData(nameof(Cases))]
    public void TypeofBranchesPreserveStructuralContracts(string source, string[] codes, bool modules)
        => Assert.Equal(codes, Check(source, modules).Select(diagnostic => diagnostic.TsCode));

    [Fact]
    public void ObjectProjectionRetainsTheCheckedInterfaceInstance()
    {
        const string source = "interface Box{field:string;}declare function take():Box|number;const value=take();if(typeof value==='object'){const correct:string=value.field;}";
        var document = new SourceDocument("interface-typeof-owner.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        var result = checker.CheckWithRecovery(statements, document);
        Assert.Empty(result.Diagnostics);
        var declaration = Assert.IsType<Stmt.Const>(statements[2]);
        var original = Assert.Single(Assert.IsType<TypeInfo.Union>(result.TypeMap.Get(declaration.Initializer)).FlattenedTypes.OfType<TypeInfo.Interface>());
        var conditional = Assert.IsType<Stmt.If>(statements[3]);
        var get = Assert.IsType<Expr.Get>(Assert.IsType<Stmt.Const>(Assert.Single(Assert.IsType<Stmt.Block>(conditional.ThenBranch).Statements)).Initializer);
        Assert.Same(original, result.TypeMap.Get(get.Object));
        Assert.IsType<TypeInfo.String>(result.TypeMap.Get(get));
        var facts = checker.EditorFacts.Freeze();
        var call = Assert.Single(facts.Invocations);
        Assert.Same(statements[1], Assert.Single(call.Candidates).Origin!.Owner);
    }

    [Fact]
    public void CallableProjectionRetainsOriginalSignaturesWithoutInventingOwners()
    {
        const string source = "interface Fn{(value:number):string;tag:string;}declare const direct:Fn;direct(1);function read(value:Fn|number){if(typeof value==='function'){value(1);}}";
        var document = new SourceDocument("callable-typeof-owner.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        var result = checker.CheckWithRecovery(statements, document);
        Assert.Empty(result.Diagnostics);
        var calls = checker.EditorFacts.Freeze().Invocations;
        Assert.Equal(2, calls.Count);
        Assert.All(calls, call => Assert.Equal(EditorInvocationStatus.Selected, call.Status));
        var original = Assert.Single(calls[0].Candidates);
        var narrowed = Assert.Single(calls[1].Candidates);
        Assert.Equal(original.OriginalSignatureId, narrowed.OriginalSignatureId);
        // Anonymous interface signatures currently publish proof/identity, without
        // a declaration source slot. Narrowing must not fabricate such an owner.
        Assert.Null(original.Origin);
        Assert.Null(narrowed.Origin);
        var direct = Assert.IsType<Expr.Call>(Assert.IsType<Stmt.Expression>(statements[2]).Expr);
        var conditional = Assert.IsType<Stmt.If>(Assert.Single(Assert.IsType<Stmt.Function>(statements[3]).Body!));
        var guarded = Assert.IsType<Expr.Call>(Assert.IsType<Stmt.Expression>(Assert.Single(Assert.IsType<Stmt.Block>(conditional.ThenBranch).Statements)).Expr);
        Assert.Same(Assert.IsType<TypeInfo.Interface>(result.TypeMap.Get(direct.Callee)), result.TypeMap.Get(guarded.Callee));
    }

    [Fact]
    public void NamedCallableProjectionRetainsTheDeclarationOwner()
    {
        const string source = "function selected(value:number):string{return 's';}selected(1);declare function take():typeof selected|number;const value=take();if(typeof value==='function'){value(2);}";
        var document = new SourceDocument("named-callable-typeof-owner.ts", source);
        var statements = new Parser(new Lexer(source).ScanTokens()).WithSourceDocument(document).WithEditorSyntax().ParseOrThrow();
        var checker = new TypeChecker(TypeCheckerOptions.Strict).WithEditorMetadata();
        Assert.Empty(checker.CheckWithRecovery(statements, document).Diagnostics);
        var calls = checker.EditorFacts.Freeze().Invocations;
        Assert.Equal(3, calls.Count);
        var selectedCalls = calls.Where(call => call.Candidates.Any(candidate =>
            ReferenceEquals(candidate.Origin?.Owner, statements[0]))).ToArray();
        Assert.Equal(2, selectedCalls.Length);
        Assert.All(selectedCalls, call => Assert.Equal(EditorInvocationStatus.Selected, call.Status));
        var original = Assert.Single(selectedCalls[0].Candidates);
        var narrowed = Assert.Single(selectedCalls[1].Candidates);
        Assert.Equal(original.OriginalSignatureId, narrowed.OriginalSignatureId);
        Assert.Same(statements[0], original.Origin!.Owner);
        Assert.Same(original.Origin.Owner, narrowed.Origin!.Owner);
    }

    [Fact]
    public void GenericProjectionKeepsTheOriginalTypeParameter()
    {
        const string source = "interface Box{field:string;}function read<T extends Box|number>(value:T){if(typeof value==='object'){const same:T=value;const correct:string=value.field;}}";
        var statements = TestHarness.ParseOrThrow(source);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        var result = checker.CheckWithRecovery(statements);
        Assert.Empty(result.Diagnostics);
        var function = Assert.IsType<Stmt.Function>(statements[1]);
        var conditional = Assert.IsType<Stmt.If>(Assert.Single(function.Body!));
        var condition = Assert.IsType<Expr.Binary>(conditional.Condition);
        var original = Assert.IsType<TypeInfo.TypeParameter>(result.TypeMap.Get(Assert.IsType<Expr.Unary>(condition.Left).Right));
        var same = Assert.IsType<Stmt.Const>(Assert.IsType<Stmt.Block>(conditional.ThenBranch).Statements[0]);
        var projected = Assert.IsType<TypeInfo.Intersection>(result.TypeMap.Get(same.Initializer));
        Assert.Same(original, Assert.Single(projected.FlattenedTypes.OfType<TypeInfo.TypeParameter>()));
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        if (!modules) return checker.CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        string path = Path.Combine(Path.GetTempPath(), "interface-typeof-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
        var entry = resolver.LoadModule(path);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}
