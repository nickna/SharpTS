using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ArrayReceiverScopeFeatureGatingTests
{
    [Theory]
    [InlineData("console.log(1);", false)]
    [InlineData("const values = [1]; console.log(values[0]);", false)]
    [InlineData("const values = [1]; values.find(value => value === 1);", true)]
    [InlineData("const Holder = class { static {} };", false)]
    [InlineData("const Holder = class { static { [1].map(value => value); } };", true)]
    [InlineData("const Holder = class Named { static { const callback = (value: number) => value; [1].map(callback); } };", true)]
    [InlineData("const values = [1]; values?.map(value => value);", true)]
    [InlineData("const values = [1]; values['reduce']((total, value) => total + value, 0);", true)]
    [InlineData("const values = [1]; const method = 'find'; values[method](() => true);", true)]
    [InlineData("const values = [1]; values.toReversed();", true)]
    [InlineData("const values = [1]; values.sort();", true)]
    [InlineData("const values: any = [undefined]; values.lastIndexOf();", true)]
    [InlineData("const values: any = [undefined]; values['indexOf']();", true)]
    [InlineData("globalThis;", true)]
    [InlineData("global;", true)]
    [InlineData("this;", true)]
    [InlineData("export const values = [undefined];", true)]
    [InlineData("(0, eval)('[1].find(value => value === 1)');", true)]
    [InlineData("const run = eval; run('[1].find(value => value === 1)');", true)]
    [InlineData("globalThis.eval('[1].find(value => value === 1)');", true)]
    [InlineData("Function('return this');", true)]
    public void DetectsReceiverScopeRequirement(string source, bool expected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var features = new RuntimeFeatureDetector().Detect(statements);
        Assert.Equal(expected, features.UsesArrayReceiverScopes);
    }

    [Fact]
    public void ConservativeFeatureSetsRetainReceiverScopes()
    {
        Assert.True(new RuntimeFeatureSet().UsesArrayReceiverScopes);
        Assert.True(RuntimeFeatureSet.EmitEverything().UsesArrayReceiverScopes);
    }

    [Fact]
    public void ProgramWithoutArrayMethodsOmitsReceiverScopeHelpers()
    {
        var (assembly, output) = TestHarness.CompileAndRun("console.log(1);", DecoratorMode.None);
        Assert.Equal("1\n", output);
        Assert.DoesNotContain(RuntimeMethods(assembly), method =>
            method.Name.EndsWith("WithReceiverScope", StringComparison.Ordinal)
            || method.Name.EndsWith("WithPrototypeReceiverScope", StringComparison.Ordinal));
        Assert.DoesNotContain(RuntimeMethods(assembly), method =>
            method.Name is "ArrayMapProto" or "ArrayFindProto"
                or "ArrayIndexOfProto" or "ArrayLastIndexOfProto");
    }

    [Fact]
    public void NestedTypedCallbackRetainsReceiverScopeAndUsesItsOwnArray()
    {
        var (assembly, output) = TestHarness.CompileAndRun("""
            const values: any = [1, 2];
            Object.defineProperty(values, 'unused', {value: 1});
            console.log(values['map'](function() {
                return [10, 20].find(value => value === 20);
            }).join(','));
            """, DecoratorMode.None);
        Assert.Equal("20,20\n", output);
        Assert.Contains(RuntimeMethods(assembly), method =>
            method.Name == "ArrayFindDirectBoolWithReceiverScope");
        Assert.DoesNotContain(RuntimeMethods(assembly), method =>
            method.Name.EndsWith("WithPrototypeReceiverScope", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Reflect.get([], 'map')", "Reflect.get([], 'lastIndexOf')")]
    [InlineData("Object.getOwnPropertyDescriptor(Object.getPrototypeOf([]), 'map').value",
        "Object.getOwnPropertyDescriptor(Object.getPrototypeOf([]), 'lastIndexOf').value")]
    public void ReflectedArrayMethodsRetainCallbackAndSearchAdapters(string mapValue, string searchValue)
    {
        string source = "const map: any = " + mapValue + "; const search: any = " + searchValue + ";" + """
            const values = [1];
            const context = {};
            console.log(map.call(values, function(value: number, index: number, array: any) {
                console.log(this === context, array === values);
                return value + 1;
            }, context).join(','));
            console.log(search.call([undefined]));
            """;
        var (assembly, output) = TestHarness.CompileAndRun(source, DecoratorMode.None);
        Assert.Equal("true true\n2\n0\n", output);
        Assert.Contains(RuntimeMethods(assembly), method => method.Name == "ArrayMapProto");
        Assert.Contains(RuntimeMethods(assembly), method => method.Name == "ArrayLastIndexOfProto");
    }

    [Fact]
    public void DynamicArraySearchPreservesOmittedSearchAndFromIndex()
    {
        var (assembly, output) = TestHarness.CompileAndRun("""
            const values: any = [undefined, 1, undefined];
            console.log(values.lastIndexOf(), values.lastIndexOf(undefined),
                values.lastIndexOf(undefined, undefined));
            console.log(values.indexOf());
            """, DecoratorMode.None);
        Assert.Equal("2 2 0\n0\n", output);
        Assert.Contains(RuntimeMethods(assembly), method => method.Name == "ArrayLastIndexOfProto");
    }

    [Fact]
    public void PrototypeInstallerPreservesMethodDescriptorsAndIdentity()
    {
        var (_, output) = TestHarness.CompileAndRun("""
            const prototype: any = Array.prototype;
            const descriptor: any = Object.getOwnPropertyDescriptor(prototype, 'map');
            const method: any = prototype.map;
            console.log(method === descriptor.value, method.name, method.length,
                descriptor.writable, descriptor.enumerable, descriptor.configurable);
            console.log(prototype[Symbol.iterator] === prototype.values);
            """, DecoratorMode.None);
        Assert.Equal("true map 1 true false true\ntrue\n", output);
    }

    private static MethodInfo[] RuntimeMethods(Assembly assembly)
        => assembly.GetType("$Runtime")!.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
}
