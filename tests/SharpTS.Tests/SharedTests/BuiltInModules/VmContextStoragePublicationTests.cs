using SharpTS.Execution;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmContextStoragePublicationTests
{
    private sealed class Target
    {
        internal readonly Dictionary<string,object?> Values = [];
        internal object? Attempt;
        internal bool Refuse;
        internal bool Throw;
    }
    private static bool Admits(object value) => value.GetType()==typeof(Target);
    private static string[] Names(object value) => ((Target)value).Values.Keys.ToArray();
    private static object?[]? Descriptor(object value,string name) => ((Target)value).Values.TryGetValue(name,out var current) ? [15,current,true,true,true,null,null] : null;
    private static bool Set(object value,string name,object? assigned)
    {
        var target=(Target)value;target.Attempt=assigned;
        if(target.Throw)throw new ThrowException("setter-abrupt");
        if(target.Refuse)return false;
        target.Values[name]=assigned;return true;
    }
    private static bool Define(object value,string name,object?[] descriptor) => Set(value,name,descriptor[1]);
    private static bool Delete(object value,string name) => ((Target)value).Values.Remove(name);
    private static bool Extensible(object value) => true;
    private static void Prevent(object value) { }
    private static VmExecutionHost Configure(Interpreter caller)
    {
        var host=caller.GetVmExecutionHost();
        VmModuleInterpreter.ConfigureExecutionHostContextStorage(host,typeof(Target),Admits,Names,Descriptor,Set,Define,Delete,Extensible,Prevent);
        return host;
    }
    private static object? Run(Interpreter caller,Target target,string code)
    {
        using var scope=caller.GetVmExecutionHost().Enter();
        return VmModuleInterpreter.ExecuteInNewContext(code,target,caller);
    }

    [Fact]
    public void FreshPromiseIsRecordedBeforeTheActualStoreConversionAndKeepsIdentity()
    {
        using var caller=new Interpreter(new StringWriter(),new StringWriter());var host=Configure(caller);
        var wrapper=new object();object? promise=null;bool recorded=false;
        VmModuleInterpreter.ConfigureExecutionHostValueAdapters(host,value=>
        {
            if(value is SharpTSPromise){recorded=VmModuleInterpreter.IsVmPromiseValue(value);promise=value;return recorded?wrapper:value;}
            return value;
        },value=>ReferenceEquals(value,wrapper)?promise:value);
        var target=new Target();var result=Run(caller,target,"globalThis.p=Promise.resolve(7);p");
        Assert.True(recorded);
        Assert.Same(wrapper,target.Values["p"]);
        Assert.Same(wrapper,host.FromVm(result));
        Assert.Same(wrapper,host.FromVm(Run(caller,target,"p")));
    }

    [Fact]
    public void IncomingObjectsRemainNegativeWhenCopiedToAnotherContextProperty()
    {
        using var caller=new Interpreter(new StringWriter(),new StringWriter());var host=Configure(caller);
        var input=new SharpTSObject(new Dictionary<string,object?>());bool upgraded=false;
        VmModuleInterpreter.ConfigureExecutionHostValueAdapters(host,value=>{if(ReferenceEquals(value,input))upgraded|=VmModuleInterpreter.IsVmValue(value);return value;},value=>value);
        var target=new Target();target.Values["input"]=input;
        Assert.Same(input,Run(caller,target,"globalThis.copy=input;copy"));
        Assert.Same(input,target.Values["copy"]);Assert.False(upgraded);Assert.False(VmModuleInterpreter.IsVmValue(input));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefusedOrAbruptNativeStoresAlreadyReceiveTheRecordedActualCallable(bool abrupt)
    {
        using var caller=new Interpreter(new StringWriter(),new StringWriter());var host=Configure(caller);
        var wrapper=new object();bool recorded=false;
        VmModuleInterpreter.ConfigureExecutionHostValueAdapters(host,value=>{if(value is SharpTSArrowFunction){recorded=VmModuleInterpreter.IsVmCallableValue(value);return recorded?wrapper:value;}return value;},value=>value);
        var target=new Target{Refuse=!abrupt,Throw=abrupt};
        if(abrupt)Assert.Equal("setter-abrupt",Assert.Throws<ThrowException>(()=>Run(caller,target,"globalThis.fn=()=>7")).Value);
        else Run(caller,target,"globalThis.fn=()=>7");
        Assert.True(recorded);Assert.Same(wrapper,target.Attempt);Assert.False(target.Values.ContainsKey("fn"));
    }
}
