using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class InterfaceTypeofGuardExecutionTests
{
    private const string Source = """
        interface Box<T>{field:T;readonly label:string;}
        function readObject(value:Box<string>|number):string{
            if(typeof value==='object')return value.field+':'+value.label;
            return String(value);
        }
        interface Fn{(value:number):string;tag:string;}
        const callback:Fn=Object.assign((value:number)=>'n'+value,{tag:'fn'});
        function readCallable(value:Fn|number):string{
            if(typeof value==='function')return value(2)+':'+value.tag;
            return String(value);
        }
        function readGeneric<T extends Box<string>|number>(value:T):string{
            if(typeof value==='object'){
                const same:T=value;
                return value.field;
            }
            return String(value);
        }
        let calls=0;
        function take():Box<string>|number{calls++;return {field:'s',label:'box'};}
        console.log(readObject(take()),readObject(3),readCallable(callback),readCallable(4),readGeneric({field:'g',label:'box'}),readGeneric(9),calls);
        """;

    [Theory, ModeData]
    public void ObjectAndCallableBranchesKeepTheirRuntimeValues(ExecutionMode mode)
        => Assert.Equal("s:box 3 n2:fn 4 g 9 1\n", TestHarness.Run(Source, mode));

    [Fact]
    public void StructuralGuardPathsHaveVerifiedCompilerIl()
        => Assert.Empty(TestHarness.CompileAndVerifyOnly(Source));

    private const string StructuralFunctionSource = "interface Box{field:string;}function kind(value:Box):string{if(typeof value==='object')return 'object';return 'function';}const callable=Object.assign(()=>0,{field:'s'});console.log(kind({field:'s'}),kind(callable));";

    [Theory, ModeData]
    public void AnInterfaceContractDoesNotReplaceTheActualRuntimeTag(ExecutionMode mode)
        => Assert.Equal("object function\n", TestHarness.Run(StructuralFunctionSource, mode));

    private const string GenericCallableSource = "function identity<T>(value:T):T{return value;}function read(value:typeof identity|number){if(typeof value==='function')return value('s');return value;}console.log(read(identity),read(3));";

    [Theory, ModeData]
    public void GenericCallableGuardsKeepTheReachableFunctionArm(ExecutionMode mode)
        => Assert.Equal("s 3\n", TestHarness.Run(GenericCallableSource, mode));

    private const string UndefinedSource = "function read(value:undefined){if(typeof value==='undefined')console.log('absent');else console.log('wrong');}read(undefined);";

    [Theory, ModeData]
    public void UndefinedGuardsKeepTheirReachableArm(ExecutionMode mode)
        => Assert.Equal("absent\n", TestHarness.Run(UndefinedSource, mode));

    private const string OpenIdentitySource = "function same<T>(value:T):T{if(typeof value==='object'){const preserved:T=value;return preserved;}return value;}const object={field:'x'};console.log(same(object)===object,same(null),same(3));";

    [Theory, ModeData]
    public void OpenGenericObjectProjectionRetainsTheSourceParameter(ExecutionMode mode)
        => Assert.Equal("true null 3\n", TestHarness.Run(OpenIdentitySource, mode));

    [Fact]
    public void ConservativeAndPrimitiveGuardPathsHaveVerifiedCompilerIl()
    {
        Assert.Empty(TestHarness.CompileAndVerifyOnly(StructuralFunctionSource));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(GenericCallableSource));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(UndefinedSource));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(OpenIdentitySource));
    }
}
