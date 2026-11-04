using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P3ClassFieldPresenceTests
{
    [Theory, ModeData]
    public void ExactTypedLocalsCannotHideInitializerObservations(ExecutionMode mode)
    {
        const string source = """
            class Json{first:number=(console.log(JSON.stringify(this)),1);later:number=7}
            const json=new Json(); console.log(json.first);
            class Read{first:any=(this as any).later;later:number=7}
            const read=new Read();console.log(read.first===undefined,read.later);
            let saved:any;
            function fail():number{throw 'stop'}
            class Abort{first:number=(saved=this,fail());later:number=7}
            try{const abort=new Abort();console.log(abort.first)}catch{}
            console.log(saved.first===undefined,saved.later===undefined);
            """;
        Assert.Equal("{}\n1\ntrue 7\ntrue true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void FieldsBecomeOwnOnlyAfterTheirInitializerCompletes(ExecutionMode mode)
    {
        const string source = """
            function inspect(value:any){
                console.log(Object.hasOwn(value,'first'),Object.hasOwn(value,'later'),
                    Object.getOwnPropertyDescriptor(value,'later')===undefined,
                    Object.keys(value).join(','),value.later===undefined);
                return 1;
            }
            class Timing{first:number=inspect(this);later:number=7;}
            const timing=new Timing();
            console.log(timing.first,timing.later,Object.keys(timing).join(','));
            """;
        Assert.Equal("false false true  true\n1 7 first,later\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AbruptInitializationLeavesOnlyCompletedDefinitions(ExecutionMode mode)
    {
        const string source = """
            let escaped:any;
            const marker={};
            function fail(value:any):number{escaped=value;throw marker}
            class Abrupt{before:number=3;first:number=fail(this);later:number=7;}
            try{new Abrupt()}catch(error){console.log(error===marker)}
            console.log(Object.hasOwn(escaped,'before'),Object.hasOwn(escaped,'first'),
                Object.hasOwn(escaped,'later'),Object.keys(escaped).join(','));
            console.log(Object.getOwnPropertyDescriptor(escaped,'first')===undefined,
                Object.getOwnPropertyDescriptor(escaped,'later')===undefined);
            """;
        Assert.Equal("true\ntrue false false before\ntrue true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void DerivedAndGenericInstancesRetainCompletedBaseFields(ExecutionMode mode)
    {
        const string source = """
            function inspect(value:any){console.log(Object.keys(value).join(','),Object.hasOwn(value,'later'));return 2}
            class Base<T>{base:T;constructor(value:T){this.base=value}}
            class Derived extends Base<number>{first:number=inspect(this);later:number=7;constructor(){super(3)}}
            const value=new Derived();
            console.log(value.base,value.first,value.later,Object.keys(value).join(','));
            class Parameter{constructor(public value:number){}}
            console.log(new Parameter(9).value,Object.keys(new Parameter(9)).join(','));
            """;
        Assert.Equal("base false\n3 2 7 base,first,later\n9 value\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ARefusedDefinitionDoesNotPublishItsField(ExecutionMode mode)
    {
        const string source = """
            let escaped:any;
            class Refused{first:number=(escaped=this,Object.preventExtensions(this),7);later:number=8}
            try{new Refused()}catch(error){console.log(error instanceof TypeError)}
            console.log(Object.keys(escaped).length,Object.hasOwn(escaped,'first'),Object.hasOwn(escaped,'later'));
            """;
        Assert.Equal("true\n0 false false\n", TestHarness.Run(source, mode));
    }
}
