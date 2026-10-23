using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class StringClassFieldGuestStorageTests
{
    [Theory]
    [InlineData("declaration")]
    [InlineData("generic")]
    [InlineData("inherited-generic")]
    public void NativeStringSettersClearRawGuestOverridesWithoutChangingTheTypedAbi(string shape)
    {
        string source = shape switch
        {
            "declaration" => "class Field { value: string = 'present'; }",
            "generic" => "class Field<T> { value: string = 'present'; }",
            _ => """
                class Base<T> { value: string = 'present'; }
                class Field<U> extends Base<U[]> {}
                """,
        };
        var assembly = CompileAndVerify(source);
        var type = Assert.Single(assembly.GetTypes(), candidate =>
            candidate.Name.Split('`')[0] == "Field");
        if (type.IsGenericTypeDefinition) type = type.MakeGenericType(typeof(string));

        var property = type.GetProperty("Value")!;
        Assert.Equal(typeof(string), property.PropertyType);
        Assert.Equal(typeof(string), Assert.Single(property.SetMethod!.GetParameters()).ParameterType);
        Assert.Contains(property.DeclaringType!.GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            field => field.Name == "__Value" && field.FieldType == typeof(string));

        var constructor = Assert.Single(type.GetConstructors(), candidate => candidate.GetParameters().Length == 0);
        object first = constructor.Invoke([]);
        object second = constructor.Invoke([]);
        var get = type.GetMethod("GetProperty")!;
        var set = type.GetMethod("SetProperty")!;
        var hasRaw = type.GetMethod("$HasRaw_value")!;

        Assert.Equal("present", get.Invoke(first, ["value"]));
        Assert.False(Assert.IsType<bool>(hasRaw.Invoke(first, [])));
        object undefined = Assert.IsAssignableFrom<object>(get.Invoke(first, ["missing"]));
        set.Invoke(first, ["value", undefined]);
        Assert.Same(undefined, get.Invoke(first, ["value"]));
        Assert.True(Assert.IsType<bool>(hasRaw.Invoke(first, [])));

        property.SetValue(first, "native");
        Assert.Equal("native", get.Invoke(first, ["value"]));
        Assert.False(Assert.IsType<bool>(hasRaw.Invoke(first, [])));

        set.Invoke(first, ["value", null]);
        Assert.Null(get.Invoke(first, ["value"]));
        Assert.True(Assert.IsType<bool>(hasRaw.Invoke(first, [])));
        property.SetValue(first, "restored");
        Assert.Equal("restored", get.Invoke(first, ["value"]));
        Assert.False(Assert.IsType<bool>(hasRaw.Invoke(first, [])));

        object token = new();
        set.Invoke(first, ["value", token]);
        Assert.Same(token, get.Invoke(first, ["value"]));
        Assert.True(Assert.IsType<bool>(hasRaw.Invoke(first, [])));
        property.SetValue(second, "second");
        Assert.Same(token, get.Invoke(first, ["value"]));
        Assert.Equal("second", get.Invoke(second, ["value"]));
        Assert.False(Assert.IsType<bool>(hasRaw.Invoke(second, [])));

        property.SetValue(first, null);
        Assert.Null(get.Invoke(first, ["value"]));
        Assert.False(Assert.IsType<bool>(hasRaw.Invoke(first, [])));
    }

    private static Assembly CompileAndVerify(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var types = new TypeChecker(TypeCheckerOptions.Strict).Check(statements);
        var compiler = new ILCompiler($"string_guest_fields_{Guid.NewGuid():N}");
        compiler.Compile(statements, types, new DeadCodeAnalyzer(types).Analyze(statements));
        var bytes = compiler.SaveToBytes();
        using var stream = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream));
        var assembly = Assembly.Load(bytes);
        Assert.All(assembly.GetReferencedAssemblies(), reference => Assert.StartsWith("System.", reference.Name!));
        return assembly;
    }
}
