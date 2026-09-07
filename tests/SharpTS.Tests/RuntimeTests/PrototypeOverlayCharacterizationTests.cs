using SharpTS.Execution;
using SharpTS.Runtime.Types;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.RuntimeTests;

// These characterize interpreter representations before extraction (#1616).
// They deliberately preserve existing differences instead of imposing new semantics.
public class PrototypeOverlayCharacterizationTests
{
    [Fact]
    public void BuiltInAssignment_UsesExistingPrototypeSpecificEnumerability()
    {
        const string source = """
            const prototypes: any[] = [String.prototype, Number.prototype, Boolean.prototype,
                Array.prototype, Object.prototype, Function.prototype, Error.prototype,
                Promise.prototype, BigInt.prototype, Symbol.prototype];
            for (const p of prototypes) {
                p.constructor = 1;
                console.log(Object.getOwnPropertyDescriptor(p, "constructor").enumerable);
                delete p.constructor;
                p.constructor = 2;
                console.log(Object.getOwnPropertyDescriptor(p, "constructor").enumerable);
            }
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("true\ntrue\n", 8)) + "false\nfalse\nfalse\nfalse\n",
            TestHarness.Run(source, ExecutionMode.Interpreted));
    }

    [Fact]
    public void ArrayLength_RemainsAVirtualBuiltInWithAnIndependentOverlay()
    {
        var prototype = new SharpTSArrayPrototype();
        Assert.True(prototype.HasOwnProperty("length"));
        Assert.Equal(0d, prototype.GetMember("length"));
        Assert.Null(prototype.GetOwnPropertyDescriptor("length"));
        prototype.SetExtra("7", 42d);
        Assert.Equal(0d, prototype.GetMember("length"));
        Assert.True(prototype.HasIndexedExtra(8));
        Assert.False(prototype.HasIndexedExtra(7));
        Assert.True(prototype.DeleteProperty("length"));
        Assert.False(prototype.HasOwnProperty("length"));
        Assert.Null(prototype.GetMember("length"));
        prototype.SetExtra("length", 3d);
        Assert.Equal(3d, prototype.GetMember("length"));
        Assert.True(prototype.GetOwnPropertyDescriptor("length")!.Enumerable);
    }

    [Fact]
    public void PrototypeState_AndConstructorIdentity_RemainRealmOwned()
    {
        using var first = new Interpreter(TextWriter.Null, TextWriter.Null);
        using var second = new Interpreter(TextWriter.Null, TextWriter.Null);
        var a = first.GetStringPrototype();
        var b = second.GetStringPrototype();
        Assert.Same(a, first.GetStringPrototype());
        Assert.Same(first.GetStringNamespace(), a.GetMember("constructor"));
        Assert.Same(second.GetStringNamespace(), b.GetMember("constructor"));
        Assert.NotSame(a, b);
        var iterator = b.GetBySymbol(SharpTSSymbol.Iterator);
        Assert.True(a.DeleteProperty("trim"));
        Assert.True(a.DeleteBySymbolStrict(SharpTSSymbol.Iterator, false));
        a.SetExtra("overlayOnly", 42d);
        Assert.True(b.HasOwnProperty("trim"));
        Assert.Same(iterator, b.GetBySymbol(SharpTSSymbol.Iterator));
        Assert.False(b.HasExtra("overlayOnly"));
        a.DefineExtraProperty("trim", new SharpTSPropertyDescriptor { Value = 5d, HasValue = true });
        Assert.Equal(5d, a.GetMember("trim"));
        Assert.IsAssignableFrom<ISharpTSCallable>(b.GetMember("trim"));
    }

    [Fact]
    public void ClassOverlay_DoesNotMutateInheritedMethodTables()
    {
        var method = SharpTSFunctionProtoToString.Instance;
        var parent = new SharpTSClass("Parent", null, new() { ["method"] = method }, [], []);
        var child = new SharpTSClass("Child", parent, [], [], []);
        var prototype = child.Prototype;
        Assert.Same(prototype, child.Prototype);
        Assert.Same(child, prototype.GetMember("constructor"));
        Assert.Same(method, prototype.GetMember("method"));
        Assert.True(prototype.HasOwnProperty("method"));
        prototype.SetExtra("method", null);
        Assert.Null(prototype.GetMember("method"));
        Assert.True(prototype.HasOwnProperty("method"));
        Assert.True(prototype.DeleteProperty("method"));
        Assert.False(prototype.HasOwnProperty("method"));
        Assert.Null(prototype.GetMember("method"));
        Assert.Same(method, child.FindMethod("method"));
        Assert.Same(method, parent.Prototype.GetMember("method"));
    }

    [Fact]
    public void StringSymbolOverlay_PreservesDescriptorsDeletionAndAccessorIdentity()
    {
        using var realm = new Interpreter(TextWriter.Null, TextWriter.Null);
        var prototype = realm.GetStringPrototype();
        var key = SharpTSSymbol.Iterator;
        var original = prototype.GetOwnPropertyDescriptor(key)!;
        Assert.True(original.Writable);
        Assert.False(original.Enumerable);
        Assert.True(original.Configurable);
        Assert.True(prototype.DeleteBySymbolStrict(key, false));
        Assert.DoesNotContain(key, prototype.GetSymbolPropertyNames());
        var accessor = SharpTSFunctionProtoToString.Instance;
        Assert.True(prototype.DefineProperty(key, new SharpTSPropertyDescriptor
        {
            Get = accessor, HasGet = true, Set = accessor, HasSet = true,
            Configurable = false, HasConfigurable = true,
        }));
        var bag = (ISharpTSSymbolPropertyBag)prototype;
        Assert.True(bag.TryGetSymbolAccessor(key, out var getter, out var setter));
        Assert.Same(accessor, getter);
        Assert.Same(accessor, setter);
        Assert.False(prototype.DeleteBySymbolStrict(key, false));
        Assert.ThrowsAny<Exception>(() => prototype.DeleteBySymbolStrict(key, true));
        Assert.False(prototype.DefineProperty(key, original));
        Assert.Contains(key, prototype.GetSymbolPropertyNames());
        Assert.Empty(prototype.OwnEnumerableKeys());
    }
}
