using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class DatePrototypeEmitCatalogTests
{
    public static IEnumerable<object[]> Methods => DatePrototypeEmitCatalog.Methods
        .Select(row => new object[] { row.JsName });

    [Theory]
    [MemberData(nameof(Methods))]
    public void SelectorRequiresTheSuppliedOwnersForwardDeclaration(string name)
    {
        var row = Assert.Single(DatePrototypeEmitCatalog.Methods, row => row.JsName == name);
        string propertyName = name == "toString" ? "ToStringMethod" : char.ToUpperInvariant(name[0]) + name[1..];
        var property = typeof(EmittedDateImplementation).GetProperty(propertyName)!;
        Assert.NotNull(property);
        var first = new EmittedDateImplementation();
        var second = new EmittedDateImplementation();
        Assert.Throws<InvalidOperationException>(() => row.Helper(first));
        Assert.Throws<InvalidOperationException>(() => row.Helper(second));
        var firstMethod = Declare(propertyName);
        var secondMethod = Declare(propertyName);
        property.SetValue(first, firstMethod);
        Assert.Same(firstMethod, row.Helper(first));
        Assert.Throws<InvalidOperationException>(() => row.Helper(second));
        property.SetValue(second, secondMethod);
        Assert.Same(secondMethod, row.Helper(second));
        Assert.Same(firstMethod, row.Helper(first));
        Assert.Throws<InvalidOperationException>(() => row.Helper(new EmittedDateImplementation()));
        Assert.Equal(name switch
        {
            "setSeconds" or "setUTCSeconds" or "setMonth" or "setUTCMonth" => 2,
            "setMinutes" or "setUTCMinutes" or "setFullYear" or "setUTCFullYear" => 3,
            "setHours" or "setUTCHours" => 4,
            "toJSON" => 1,
            _ when name.StartsWith("set", StringComparison.Ordinal) => 1,
            _ => 0,
        }, row.Length);
    }

    [Fact]
    public void WiringDefinitionsAreImmutableAndCopiesCannotAlterLaterEmission()
    {
        var original = DatePrototypeEmitCatalog.Methods;
        Assert.Equal(43, original.Length);
        Assert.Equal(43, original.Select(row => row.JsName).Distinct(StringComparer.Ordinal).Count());
        var list = Assert.IsAssignableFrom<IList>(original);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = original[1]);
        Assert.Throws<NotSupportedException>(list.Clear);
        var changed = original.SetItem(0, original[1]);
        Assert.Equal("valueOf", changed[0].JsName);
        Assert.Equal("getTime", DatePrototypeEmitCatalog.Methods[0].JsName);
        Assert.Equal(original, DatePrototypeEmitCatalog.Methods);
    }

    private static MethodBuilder Declare(string name)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"date_catalog_{Guid.NewGuid():N}"), typeof(object).Assembly);
        return assembly.DefineDynamicModule("main").DefineType("Runtime")
            .DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object)]);
    }
}
