using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P3ClassExpressionHeritageBindingTests
{
    [Theory, ModeData]
    public void NamedClassHeritageSeesTheUninitializedSelfBinding(ExecutionMode mode)
    {
        const string source = """
            try{var value=(class value extends value{});}catch(error){console.log(error instanceof ReferenceError)}
            const Outer=class{};
            try{const value=class Outer extends Outer{};}catch(error){console.log(error instanceof ReferenceError)}
            try{const value=class Inner extends (()=>Inner)(){};}catch(error){console.log(error instanceof ReferenceError)}
            console.log(typeof Outer);
            """;
        Assert.Equal("true\ntrue\ntrue\nfunction\n", TestHarness.RunUnchecked(source, mode));
    }

    [Theory, ModeData]
    public void HeritageCapturesAndInitializedSelfRemainAvailableInTheClassBody(ExecutionMode mode)
    {
        const string source = """
            class Base{}
            function make(base){
                const marker=7;
                return class Named extends base{
                    value=marker;
                    static self=Named;
                    self(){return Named}
                };
            }
            const Derived=make(Base),value=new Derived();
            console.log(value.value,Derived.self===Derived,value.self()===Derived,value instanceof Base);
            """;
        Assert.Equal("7 true true true\n", TestHarness.RunUnchecked(source, mode));
    }
}
