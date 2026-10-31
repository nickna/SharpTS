using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class CallableMetadataDescriptor3209RegressionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-reported-map", "const C:any=Map;Object.defineProperty(C,\"length\",{value:9,configurable:true});console.log(C.length,Object.getOwnPropertyDescriptor(C,\"length\")?.configurable);", "9 true\n"];
        yield return ["regexp-compatible-length", "const C:any=RegExp;Object.defineProperty(C,\"length\",{value:9,configurable:true});console.log(C.length,Object.getOwnPropertyDescriptor(C,\"length\")?.configurable);", "9 true\n"];
        yield return ["ordinary-function-control", "function f(value:number){}const d:any=Object.getOwnPropertyDescriptor(f,\"length\");console.log(d.value,d.writable,d.enumerable,d.configurable);", "1 false false true\n"];
        yield return ["native-method-control", "const d:any=Object.getOwnPropertyDescriptor(Date.now,\"length\");console.log(d.value,d.writable,d.enumerable,d.configurable);", "0 false false true\n"];
        yield return ["native-name-control", "const C:any=Map;Object.defineProperty(C,\"name\",{value:\"Changed\",configurable:true});const d:any=Object.getOwnPropertyDescriptor(C,\"name\");console.log(C.name,d.writable,d.enumerable,d.configurable);", "Changed false false true\n"];
    }

    [Theory, MemberData(nameof(Cases))]
    public void CompatibleDefinitionsConsumeCompleteMetadata(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }

    [Fact]
    public void SharedMetadataRecordSuppliesEveryDataField()
    {
        using var interpreter = new SharpTS.Execution.Interpreter();
        var callable = new SharpTS.Runtime.Types.SharpTSBuiltInConstructor("Map", _ => null);
        var descriptor = SharpTS.Runtime.BuiltIns.ObjectBuiltIns.OwnPropertyDescriptorOf(
            interpreter, callable, "length");
        Assert.NotNull(descriptor);
        Assert.True(descriptor.HasValue);
        Assert.True(descriptor.HasWritable);
        Assert.True(descriptor.HasEnumerable);
        Assert.True(descriptor.HasConfigurable);
        Assert.Equal(0d, Convert.ToDouble(descriptor.Value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.False(descriptor.Writable);
        Assert.False(descriptor.Enumerable);
        Assert.True(descriptor.Configurable);
    }
}