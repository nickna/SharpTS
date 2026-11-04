using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class LexicalSuperParameterPropertyTests
{
    [Theory, ModeData]
    public void CurrentParameterBindingsSurviveArrowAndBlockShadows(ExecutionMode mode)
    {
        const string source = """
            const C=class extends class{} {
                constructor(public value:number=1){
                    value=9;
                    {
                        const value=50;
                        const run=(value:number)=>{const invoke=()=>super();value=99;return invoke()};
                        run(value);
                    }
                }
            };
            console.log(new C(1).value);
            """;
        Assert.Equal("9\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void FieldInitializersRetainOuterBindings(ExecutionMode mode)
    {
        const string source = """
            let value=4;
            const C=class extends class{} {
                field=value;
                constructor(public value:number=1){const run=()=>super();value=9;run()}
            };
            const item=new C();
            console.log(item.field,item.value);
            """;
        Assert.Equal("4 9\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void InitializersCanMutateTheParameterBeforeItsPropertyAssignment(ExecutionMode mode)
    {
        const string source = """
            let change;
            function initialize(){change();return 7}
            const C=class extends class{} {
                field=initialize();
                constructor(public value:number=1){change=()=>value=11;const run=()=>super();run()}
            };
            const item=new C();
            console.log(item.field,item.value);
            """;
        Assert.Equal("7 11\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void DirectAndLexicalSuperUseTheSameCurrentParameters(ExecutionMode mode)
    {
        const string source = """
            const C=class extends class{} {
                field=7;
                constructor(public value:number=1,arrow:boolean=false){
                    const run=()=>super();value=9;if(arrow)run();else super();
                }
            };
            console.log(new C(1,false).value,new C(1,true).value);
            """;
        Assert.Equal("9 9\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void StrictConstructorArgumentsRemainIndependentFromParameterCells(ExecutionMode mode)
    {
        const string source = """
            const C=class extends class{} {
                constructor(public value:number){arguments[0]=99;const run=()=>super();run()}
            };
            console.log(new C(1).value);
            """;
        Assert.Equal("1\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void RepeatedOrAbruptSuperDoesNotRepublishParameterProperties(ExecutionMode mode)
    {
        const string source = """
            let parents=0,fields=0;
            const replacement={};
            const C=class extends class{constructor(){parents++;return replacement}} {
                field=(fields++,7);
                constructor(public value:number=3){
                    const run=()=>super();run();value=9;
                    try{run()}catch(error){console.log(error instanceof ReferenceError)}
                }
            };
            const item=new C();console.log(item===replacement,item.value,parents,fields);
            let escaped;const failure={};
            function fail(receiver){escaped=receiver;throw failure}
            const Abrupt=class extends class{} {
                first=fail(this);later=8;
                constructor(public value:number=2){
                    const run=()=>super();try{run()}catch(error){console.log(error===failure)}
                    try{run()}catch(error){console.log(error instanceof ReferenceError)}
                }
            };
            new Abrupt();
            console.log(escaped.value===undefined,Object.hasOwn(escaped,'first'),Object.hasOwn(escaped,'later'));
            """;
        Assert.Equal("true\ntrue 3 2 1\ntrue\ntrue\ntrue false false\n", TestHarness.RunUnchecked(source, mode));
    }

    // Interpreter declaration chronology is independently tracked by #3482.
    [Theory, CompiledOnlyData]
    public void ParameterDeclarationsPrecedeOrdinaryFieldsAndSurviveAbruptInitialization(ExecutionMode mode)
    {
        const string source = """
            const C=class extends class{} {
                present=Object.hasOwn(this,'value');
                constructor(public value:number=1){const run=()=>super();run()}
            };
            const item=new C();console.log(item.present,Object.keys(item).join(','));
            let escaped;
            function fail(receiver){escaped=receiver;throw 1}
            const Abrupt=class extends class{} {
                first=fail(this);later=8;
                constructor(public value:number=2){try{(()=>super())()}catch{}}
            };
            new Abrupt();
            console.log(Object.hasOwn(escaped,'value'),escaped.value===undefined,
                Object.hasOwn(escaped,'first'),Object.hasOwn(escaped,'later'),Object.keys(escaped).join(','));
            """;
        Assert.Equal("true value,present\ntrue true false false value\n", TestHarness.RunUnchecked(source, mode));
    }
}
