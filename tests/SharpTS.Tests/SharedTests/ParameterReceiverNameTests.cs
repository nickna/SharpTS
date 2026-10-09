using System.Reflection;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ParameterReceiverNameTests
{
    [Theory, ModeData]
    public void SourceReceiverSpellingPreservesFunctionValueArguments(ExecutionMode mode)
    {
        const string source = """
            function echo(__this: number): number { return __this; }
            const value: any = echo;
            console.log(Reflect.apply(echo, null, [42]));
            console.log(value.call(null, 43));
            console.log(value.apply(null, [44]));
            console.log(value.bind(null, 45)());
            console.log(value(46));
            """;
        Assert.Equal("42\n43\n44\n45\n46\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SourceStringReceiverSpellingDoesNotApplyBuiltinReceiverGuard(ExecutionMode mode)
    {
        const string source = """
            function text(__this: string): string { return __this; }
            console.log(Reflect.apply(text, null, ["hello"]));
            const omitted: any = null;
            console.log(Reflect.apply(text, null, [omitted]));
            """;
        Assert.Equal("hello\nnull\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SourceRestReceiverSpellingIsStillARestParameter(ExecutionMode mode)
    {
        const string source = """
            function values(...__this: number[]): string {
                return __this.length + ":" + __this[0];
            }
            console.log(Reflect.apply(values, null, [7, 8, 9]));
            console.log(values.apply(null, [4, 5]));
            """;
        Assert.Equal("3:7\n2:4\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void SourceMethodAndAccessorReceiverSpellingPreservesArguments(ExecutionMode mode)
    {
        const string source = """
            let observed: number = 0;
            class Counter {
                add(__this: number): number { return 11 + __this; }
                static echo(__this: number): number { return __this; }
                set amount(__this: number) { observed = __this; }
                static set other(__value: number) { console.log(__value); }
            }
            const counter = new Counter();
            counter.amount = 11;
            console.log(observed);
            console.log(Reflect.apply(counter.add, counter, [5]));
            console.log(Reflect.apply(Counter.echo, null, [17]));
            Counter.other = 19;
            """;
        Assert.Equal("11\n16\n17\n19\n", TestHarness.Run(source, mode));
    }

    [Theory]
    [InlineData("set_Amount", "__this", false)]
    [InlineData("$static_set_Other", "__value", true)]
    public void CompiledAccessorWrappersPreserveSourceNamesLengthAndRouting(string methodName, string parameterName, bool isStatic)
    {
        const string source = """
            let observed: number = 0;
            class Counter {
                set amount(__this: number) { observed = __this; }
                static set other(__value: number) { observed = __value; }
            }
            function read(): number { return observed; }
            """;
        var (assembly, output) = TestHarness.CompileAndRun(source, DecoratorMode.None);
        Assert.Empty(output);
        const BindingFlags methods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var method = assembly.GetTypes().SelectMany(type => type.GetMethods(methods))
            .Single(method => method.Name == methodName);
        Assert.Equal(parameterName, Assert.Single(method.GetParameters()).Name);
        Assert.Contains(method.GetCustomAttributes(false), attribute => attribute.GetType().Name == "$SourceParameters");
        var function = assembly.GetType("$TSFunction")!;
        var target = isStatic ? null : Activator.CreateInstance(method.DeclaringType!);
        var wrapper = Activator.CreateInstance(function, new object?[] { target, method })!;
        Assert.False((bool)function.GetField("_expectsThis")!.GetValue(wrapper)!);
        Assert.Equal(1, function.GetMethod("get_Length")!.Invoke(wrapper, null));
        function.GetMethod("InvokeWithThis")!.Invoke(wrapper, [target, new object[] { 23d }]);
        var read = assembly.GetType("$Program")!.GetMethods(methods).Single(method => method.Name.EndsWith("read", StringComparison.Ordinal));
        Assert.Equal(23d, read.Invoke(null, null));
    }

    [Theory, ModeData]
    public void SyntheticSourceAndBuiltinReceiversRemainRecognized(ExecutionMode mode)
    {
        const string source = """
            const value: any = function(input: number): number { return this.offset + input; };
            console.log(Reflect.apply(value, {offset: 10}, [5]));
            console.log(Reflect.apply(Array.prototype.join, [1, 2], [":"]));
            console.log(Reflect.apply(Array.prototype.values, [7], []).next().value);
            try { Reflect.apply(String.prototype.indexOf, null, ["x"]); }
            catch (error) { console.log(error instanceof TypeError); }
            """;
        Assert.Equal("15\n1:2\n7\ntrue\n", TestHarness.Run(source, mode));
    }
}
