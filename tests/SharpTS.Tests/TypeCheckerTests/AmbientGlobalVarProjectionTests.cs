using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class AmbientGlobalVarProjectionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["ambient-dot", "declare var ambientCounter:number;const value:number=globalThis.ambientCounter;", new string[] {  }];
        yield return ["ambient-literal-index", "declare var ambientText:string;const value:string=globalThis['ambientText'];", new string[] {  }];
        yield return ["forward-ambient", "const value:number=globalThis.ambientCounter;declare var ambientCounter:number;", new string[] {  }];
        yield return ["comma-ambient", "declare var ambientCounter:number,ambientText:string;const a:number=globalThis.ambientCounter;const b:string=globalThis.ambientText;", new string[] {  }];
        yield return ["global-type-alias", "declare var ambientCounter:number;declare const alias:typeof globalThis;const value:number=alias.ambientCounter;", new string[] {  }];
        yield return ["global-indexed-type", "declare var ambientCounter:number;type Value=(typeof globalThis)['ambientCounter'];const value:Value=7;", new string[] {  }];
        yield return ["ordinary-script-var", "var scriptCounter:number=7;const value:number=globalThis.scriptCounter;", new string[] {  }];
        yield return ["local-shadow-keeps-global-type", "declare var ambientCounter:number;function read(){const ambientCounter='local';const value:number=globalThis.ambientCounter;return ambientCounter;}", new string[] {  }];
        yield return ["global-augmentation", "export {};declare global {var augmentedCounter:number;}const value:number=globalThis.augmentedCounter;", new string[] {  }];
        yield return ["ambient-let-excluded", "declare let lexicalCounter:number;globalThis.lexicalCounter;", new string[] { "TS2339" }];
        yield return ["ambient-const-excluded", "declare const lexicalCounter:number;globalThis.lexicalCounter;", new string[] { "TS2339" }];
        yield return ["script-let-excluded", "let lexicalCounter=7;globalThis.lexicalCounter;", new string[] { "TS2339" }];
        yield return ["script-const-excluded", "const lexicalCounter=7;globalThis.lexicalCounter;", new string[] { "TS2339" }];
        yield return ["module-ambient-excluded", "export {};declare var moduleCounter:number;globalThis.moduleCounter;", new string[] { "TS7017" }];
        yield return ["module-var-excluded", "export {};var moduleCounter=7;globalThis.moduleCounter;", new string[] { "TS7017" }];
        yield return ["nested-var-excluded", "function read(){var nestedCounter=7;}globalThis.nestedCounter;", new string[] { "TS7017" }];
        yield return ["precise-read-type", "declare var ambientCounter:number;const value:string=globalThis.ambientCounter;", new string[] { "TS2322" }];
        yield return ["precise-write-type", "declare var ambientCounter:number;globalThis.ambientCounter='wrong';", new string[] { "TS2322" }];
        yield return ["comma-lexicals-excluded", "declare let lexicalFirst:number,lexicalSecond:string;globalThis.lexicalSecond;", new string[] { "TS2339" }];
    }

    [Theory, MemberData(nameof(Cases))]
    public void OnlySourceGlobalVarDeclarationsEnterTheGlobalObject(string name, string source, string[] expected)
    {
        Assert.NotEmpty(name);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var entry = new ParsedModule("main.ts", statements) { IsScript = ScriptDetector.IsScriptFile(statements) };
        var library = new ParsedModule("lib.global.d.ts", new Parser(new Lexer("declare var libraryCounter:number;").ScanTokens()).ParseOrThrow())
            { IsDefaultLibrary = true, IsDeclarationFile = true };
        var checker = new TypeChecker(new TypeCheckerOptions { NoImplicitAny = true, StrictNullChecks = true });
        checker.CheckModules([library,entry], new ModuleResolver(Path.GetTempPath()));
        var actual = checker.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(diagnostic => diagnostic.TsCode).OrderBy(code => code).ToArray();
        Assert.Equal(expected.OrderBy(code => code), actual);
    }

    [Fact]
    public void ActualAmbientKeywordSurvivesWithoutChangingLegacyMergeFlags()
    {
        var statements = new Parser(new Lexer("declare var first:number,second:string;declare let third:number;declare const fourth:number;declare var Symbol:any;").ScanTokens()).ParseOrThrow();
        var variables = statements.SelectMany(statement => statement is Stmt.Sequence sequence ? sequence.Statements : [statement]).Cast<Stmt.Var>().ToArray();
        Assert.Equal(new[] { true,true,false,false,true }, variables.Select(variable => variable.IsAmbientVar));
        Assert.Equal(new[] { false,false,false,false,true }, variables.Select(variable => variable.IsVar));
        Assert.All(variables, variable => Assert.True(variable.IsDeclare));
    }
}
