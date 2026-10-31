using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class OptionalSpreadPresenceTests
{
    [Theory]
    [InlineData("const maybe:{type?:'size'}={}; const value:{type:'size'}={type:'size',...maybe};")]
    [InlineData("interface Maybe {type?:'size'} const maybe:Maybe={}; const value:{type:'size'}={type:'size',...maybe};")]
    [InlineData("interface Base {value?:number} interface Maybe extends Base {} declare const maybe:Maybe; const value:{value:string|number}={value:'before',...maybe};")]
    [InlineData("declare const maybe:{value?:number}; const value:{value:string|number}={value:'before',...maybe};")]
    [InlineData("declare const first:{value?:string}; declare const second:{value?:number}; const value:{value?:string|number}={...first,...second};")]
    [InlineData("declare const maybe:{value?:string}; const value:{value:number}={...maybe,value:1};")]
    [InlineData("declare const maybe:{value?:string}; const required={value:1}; const value:{value:number}={...maybe,...required};")]
    [InlineData("declare const maybe:{value:number}|undefined; const value:{value:string|number}={value:'before',...maybe};")]
    [InlineData("declare const maybe:{value:number}|null; const value:{value?:number}={...maybe};")]
    [InlineData("declare const maybe:{value?:number}|{value?:boolean}; const value:{value?:number|boolean}={...maybe};")]
    [InlineData("declare const maybe:{value?:number}&{extra:boolean}; const value:{value:string|number,extra:boolean}={value:'before',...maybe};")]
    [InlineData("declare const maybe:{value:number}&{value?:number}; const value:{value:number}={...maybe};")]
    public void OptionalOverridesRetainEarlierPresenceAndAllPossibleValues(string source)
    {
        Check(source, TypeCheckerOptions.Strict);
    }

    [Theory]
    [InlineData("declare const maybe:{value?:number}; const value:{value:string}={value:'before',...maybe};")]
    [InlineData("declare const maybe:{value?:number}; const value:{value:number}={value:'before',...maybe};")]
    [InlineData("declare const first:{value?:string}; declare const second:{value?:number}; const value:{value:string|number}={...first,...second};")]
    [InlineData("declare const maybe:{value:number}|undefined; const value:{value:number}={...maybe};")]
    [InlineData("declare const maybe:{value?:number}|{value?:boolean}; const value:{value:number|boolean}={...maybe};")]
    [InlineData("declare const maybe:{value?:string}; const value:{value:string}={...maybe,value:1};")]
    public void OptionalSourcesCannotInventPresenceOrEraseAnAlternativeValue(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var result = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(statements);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.TsCode == "TS2322");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitUndefinedFollowsTheSelectedExactOptionalPolicy(bool exact)
    {
        const string prelude = "declare const maybe:{value?:undefined}; const merged={value:'before' as const,...maybe};";
        Check(prelude + "const value:'before'|undefined=merged.value;", TypeCheckerOptions.Strict with { ExactOptionalPropertyTypes = exact });
        var statements = new Parser(new Lexer(prelude + "const value:'before'=merged.value;").ScanTokens()).ParseOrThrow();
        var result = new TypeChecker(TypeCheckerOptions.Strict with { ExactOptionalPropertyTypes = exact }).CheckWithRecovery(statements);
        Assert.Equal(exact, result.Diagnostics.Any(diagnostic => diagnostic.TsCode == "TS2322"));
    }

    private static void Check(string source, TypeCheckerOptions options)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var checker = new TypeChecker(options);
        checker.Check(statements);
        Assert.Empty(checker.GetDiagnostics());
    }
}
