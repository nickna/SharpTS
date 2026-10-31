using SharpTS.Execution;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules.Interpreter;
using SharpTS.Runtime.Exceptions;
using SharpTS.Runtime.Types;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmContextStorageTests
{
    private sealed class Store
    {
        internal readonly SharpTSObject Record = new([]);
        internal object?[]? WireOverride;
        internal bool ThrowOnSet;
        internal object? ThrownValue;
    }
    private sealed class Carrier(object? value) : Exception { internal readonly object? Value = value; }
    private static bool Admits(object target) => target.GetType() == typeof(Store);
    private static string[] Keys(object target) => ((Store)target).Record.OwnVisibleStringKeys().ToArray();
    private static object?[]? Read(object target, string name)
    {
        var store = (Store)target;
        if (store.WireOverride is { } wire) return wire;
        var descriptor = store.Record.GetOwnPropertyDescriptor(name);
        return descriptor is null ? null : Encode(descriptor);
    }
    private static object?[] Encode(SharpTSPropertyDescriptor descriptor) =>
        [(descriptor.HasValue ? 1 : 0) | (descriptor.HasWritable ? 2 : 0) | (descriptor.HasEnumerable ? 4 : 0)
         | (descriptor.HasConfigurable ? 8 : 0) | (descriptor.HasGet ? 16 : 0) | (descriptor.HasSet ? 32 : 0),
         descriptor.Value, descriptor.Writable, descriptor.Enumerable, descriptor.Configurable,
         descriptor.RawGet ?? descriptor.Get ?? (object)SharpTSUndefined.Instance,
         descriptor.RawSet ?? descriptor.Set ?? (object)SharpTSUndefined.Instance];
    private static bool Set(object target, string name, object? value)
    {
        var store = (Store)target;
        if (store.ThrowOnSet) throw new Carrier(store.ThrownValue);
        return store.Record.TrySetProperty(name, value);
    }
    private static bool Define(object target, string name, object?[] wire)
    {
        int mask = (int)wire[0]!;
        return ((Store)target).Record.DefineProperty(name, new SharpTSPropertyDescriptor
        {
            HasValue = (mask & 1) != 0, Value = wire[1], HasWritable = (mask & 2) != 0, Writable = (bool)wire[2]!,
            HasEnumerable = (mask & 4) != 0, Enumerable = (bool)wire[3]!, HasConfigurable = (mask & 8) != 0,
            Configurable = (bool)wire[4]!, HasGet = (mask & 16) != 0, RawGet = wire[5], Get = wire[5] as ISharpTSCallable,
            HasSet = (mask & 32) != 0, RawSet = wire[6], Set = wire[6] as ISharpTSCallable,
        });
    }
    private static bool Delete(object target, string name) => ((Store)target).Record.DeleteProperty(name);
    private static bool Extensible(object target) => ((Store)target).Record.IsExtensible;
    private static void Prevent(object target) => ((Store)target).Record.PreventExtensions();
    private static void Configure(VmExecutionHost host, object? domain = null) =>
        VmModuleInterpreter.ConfigureExecutionHostContextStorage(host, domain ?? typeof(Store), Admits, Keys, Read, Set, Define, Delete, Extensible, Prevent);
    private static SharpTSPropertyDescriptor Data(object? value, bool writable = true, bool enumerable = true) => new()
    {
        HasValue = true, Value = value, HasWritable = true, Writable = writable,
        HasEnumerable = true, Enumerable = enumerable, HasConfigurable = true, Configurable = true,
    };
    private static object? Run(Interpreter caller, Store context, string source)
    {
        using var scope = caller.GetVmExecutionHost().Enter();
        return VmModuleInterpreter.ExecuteInNewContext(source, context, caller);
    }

    [Fact]
    public void ReadonlyAndHiddenDescriptorsComeFromActualStorage()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        Configure(caller.GetVmExecutionHost());
        var store = new Store();
        store.Record.DefineProperty("foo", Data(7d, writable: false, enumerable: false));
        Assert.Equal("7", Run(caller, store, "foo=9;String(foo)"));
        Assert.Equal(7d, store.Record.GetProperty("foo"));
        Assert.Equal("false", Run(caller, store, "String(Object.getOwnPropertyDescriptor(globalThis,'foo').enumerable)"));
    }

    [Fact]
    public void SeedingAndKeyEnumerationDoNotReadAccessorValues()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        Configure(caller.GetVmExecutionHost());
        var store = new Store();
        int reads = 0;
        var getter = BuiltInMethod.CreateV2("get foo", 0, (_, receiver, _) => { Assert.Same(store, receiver.ToObject()); reads++; return RuntimeValue.FromBoxed(7d); });
        store.Record.DefineProperty("foo", new SharpTSPropertyDescriptor { HasGet = true, Get = getter, RawGet = getter, HasEnumerable = true, Enumerable = true, HasConfigurable = true, Configurable = true });
        Assert.Equal(true, Run(caller, store, "Object.getOwnPropertyNames(globalThis).includes('foo')"));
        Assert.Equal(0, reads);
        Assert.Equal(7d, Run(caller, store, "foo"));
        Assert.Equal(1, reads);
    }

    [Fact]
    public void SetterUsesOriginalReceiverOnceWithoutWriteBackReplay()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        Configure(caller.GetVmExecutionHost());
        var store = new Store();
        int writes = 0;
        var setter = BuiltInMethod.CreateV2("set foo", 1, (_, receiver, args) => { Assert.Same(store, receiver.ToObject()); Assert.Equal(9d, args[0].ToObject()); writes++; return RuntimeValue.Undefined; });
        store.Record.DefineProperty("foo", new SharpTSPropertyDescriptor { HasSet = true, Set = setter, RawSet = setter, HasConfigurable = true, Configurable = true });
        Run(caller, store, "foo=9");
        Assert.Equal(1, writes);
    }

    [Fact]
    public void LiveOutsideMutationAndSameDomainReusePreserveTheDataTarget()
    {
        var store = new Store();
        store.Record.SetProperty("foo", 7d);
        using var first = new Interpreter(new StringWriter(), new StringWriter());
        Configure(first.GetVmExecutionHost());
        var read = Run(first, store, "()=>foo")!;
        store.Record.SetProperty("foo", 8d);
        Assert.Equal(8d, VmModuleInterpreter.InvokeVmCallable(read, SharpTSUndefined.Instance, []));
        using var second = new Interpreter(new StringWriter(), new StringWriter());
        Configure(second.GetVmExecutionHost());
        Assert.Equal(8d, Run(second, store, "foo"));
        store.Record.DeleteProperty("foo");
        Assert.Equal("undefined", Run(second, store, "typeof foo"));
    }

    [Fact]
    public void PartialDefinesKeepOmittedValuesAndUndefinedAccessorPresence()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        Configure(caller.GetVmExecutionHost());
        var store = new Store();
        store.Record.DefineProperty("foo", Data(7d, enumerable: false));
        Assert.Equal("true|7|true", Run(caller, store, "[Reflect.defineProperty(globalThis,'foo',{enumerable:true}),foo,Object.getOwnPropertyDescriptor(globalThis,'foo').writable].join('|')"));
        Run(caller, store, "Object.defineProperty(globalThis,'accessor',{get:undefined,configurable:true})");
        var descriptor = store.Record.GetOwnPropertyDescriptor("accessor")!;
        Assert.True(descriptor.HasGet);
        Assert.False(descriptor.HasValue);
    }

    [Fact]
    public void FreshDescriptorAccessorsHaveActualOperationReceiptsBeforeLeavingCore()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var host = caller.GetVmExecutionHost();
        Configure(host);
        int recorded = 0;
        VmModuleInterpreter.ConfigureExecutionHostValueAdapters(host, value =>
        {
            if (value is ISharpTSCallable)
            {
                Assert.True(VmModuleInterpreter.IsVmValue(value));
                recorded++;
            }
            return value;
        }, value => value);
        var store = new Store();
        Run(caller, store, "Object.defineProperty(globalThis,'fresh',{get(){return 7},set(value){},configurable:true})");
        Assert.Equal(2, recorded);
        Assert.Equal(7d, Run(caller, store, "fresh"));
    }
    [Fact]
    public void NonextensibleOutsideObjectRetainsSeparateInternalGlobalSlots()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        Configure(caller.GetVmExecutionHost());
        var store = new Store();
        store.Record.PreventExtensions();
        Assert.Equal("1", Run(caller, store, "globalThis.foo=1;String(foo)"));
        Assert.False(store.Record.HasProperty("foo"));
    }

    [Fact]
    public void ActualThrownValueAdapterRunsOnceAndPreservesThePayload()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var host = caller.GetVmExecutionHost();
        Configure(host);
        var marker = new SharpTSObject(new Dictionary<string, object?>());
        var store = new Store { ThrowOnSet = true, ThrownValue = marker };
        store.Record.SetProperty("marker", marker);
        store.Record.SetProperty("foo", 7d);
        int predicates = 0, getters = 0;
        VmModuleInterpreter.ConfigureExecutionHostThrownValueAdapter(host, error => { predicates++; return error is Carrier; }, error => { getters++; return ((Carrier)error).Value; });
        Assert.Equal(true, Run(caller, store, "let caught=false;try{foo=9}catch(error){caught=error===marker}caught"));
        Assert.Equal(1, predicates);
        Assert.Equal(1, getters);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MalformedDescriptorWireIsRejectedWithoutCoercion(int mode)
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        Configure(caller.GetVmExecutionHost());
        var store = new Store { WireOverride = mode switch { 0 => [1], 1 => [64, null, false, false, false, null, null], _ => [1, 7d, "false", false, false, null, null] } };
        store.Record.SetProperty("foo", 7d);
        Assert.Throws<InvalidOperationException>(() => Run(caller, store, "foo"));
    }

    [Fact]
    public void ConfigurationRequiresStaticCallbacksAndAnUnstartedOwnedHost()
    {
        using var caller = new Interpreter(new StringWriter(), new StringWriter());
        var host = caller.GetVmExecutionHost();
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ConfigureExecutionHostContextStorage(new object(), typeof(Store), Admits, Keys, Read, Set, Define, Delete, Extensible, Prevent));
        var captured = new Store();
        Assert.Throws<ArgumentException>(() => VmModuleInterpreter.ConfigureExecutionHostContextStorage(host, typeof(Store), value => ReferenceEquals(value, captured), Keys, Read, Set, Define, Delete, Extensible, Prevent));
        Configure(host);
        Assert.Throws<InvalidOperationException>(() => Configure(host));
        using var startedCaller = new Interpreter(new StringWriter(), new StringWriter());
        var started = startedCaller.GetVmExecutionHost();
        using (started.Enter()) { }
        Assert.Throws<InvalidOperationException>(() => Configure(started));
        Assert.False(VmContext.ApplyGlobalObjectState(new object(), caller));
        host.Dispose();
        Assert.Throws<ObjectDisposedException>(() => Configure(host));
    }
}