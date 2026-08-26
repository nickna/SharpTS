using System.Reflection;
using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class Issue1486ThrowWrapperTests
{
    [Fact]
    public void CreateException_UsesDedicatedCarrierWithoutEagerMessageOrMetadata()
    {
        Assembly assembly = Compile("function noop(): void { }");
        MethodInfo createException = assembly.GetType("$Runtime")!
            .GetMethod("CreateException", BindingFlags.Public | BindingFlags.Static)!;
        var probe = new ToStringProbe();

        var exception = Assert.IsAssignableFrom<Exception>(
            createException.Invoke(null, [probe]));

        Assert.Equal("$ThrownValueException", exception.GetType().Name);
        Assert.Same(probe, exception.GetType().GetProperty("Value")!.GetValue(exception));
        Assert.Equal(0, probe.CallCount);
        Assert.Empty(exception.Data);
        Assert.Equal("probe", exception.Message);
        Assert.Equal(1, probe.CallCount);
    }

    [Fact]
    public void ReflectionAndManagedHostUnwrapping_PreserveExactGuestValue()
    {
        Assembly assembly = Compile("""
            const marker: any = {};
            function getMarker(): any { return marker; }
            function fail(): void { throw marker; }
            """);
        Type program = assembly.GetType("$Program")!;
        program.GetMethod("Main", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
        MethodInfo getMarker = FindFunction(program, "getMarker");
        MethodInfo fail = FindFunction(program, "fail");
        object marker = getMarker.Invoke(null, null)!;
        var reflectionException = Assert.Throws<TargetInvocationException>(
            () => fail.Invoke(null, null));

        MethodInfo emittedWrap = assembly.GetType("$Runtime")!
            .GetMethod("WrapException", BindingFlags.Public | BindingFlags.Static)!;
        Assert.Same(marker, emittedWrap.Invoke(null, [reflectionException]));
        Assert.Same(marker, RuntimeTypes.WrapException(reflectionException));
    }

    private static Assembly Compile(string source)
    {
        var result = CompilationService.Compile(source);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return Assembly.Load(result.AssemblyBytes!);
    }

    private static MethodInfo FindFunction(Type program, string name) =>
        program.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name == name ||
                method.Name.EndsWith("_" + name, StringComparison.Ordinal));

    private sealed class ToStringProbe
    {
        public int CallCount { get; private set; }

        public override string ToString()
        {
            CallCount++;
            return "probe";
        }
    }
}
