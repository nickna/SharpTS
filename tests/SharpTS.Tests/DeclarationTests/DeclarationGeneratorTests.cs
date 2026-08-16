using SharpTS.Declaration;
using Xunit;

namespace SharpTS.Tests.DeclarationTests;

/// <summary>
/// Tests for the TypeScript declaration generator from .NET types.
/// </summary>
public class DeclarationGeneratorTests
{
    #region Type Mapping Tests

    [Theory]
    [InlineData(typeof(void), "void")]
    [InlineData(typeof(string), "string")]
    [InlineData(typeof(bool), "boolean")]
    [InlineData(typeof(int), "number")]
    [InlineData(typeof(long), "number")]
    [InlineData(typeof(double), "number")]
    [InlineData(typeof(float), "number")]
    [InlineData(typeof(decimal), "number")]
    [InlineData(typeof(object), "unknown")]
    public void DotNetTypeMapper_MapsPrimitives(Type dotNetType, string expectedTs)
    {
        var result = DotNetTypeMapper.MapToTypeScript(dotNetType);
        Assert.Equal(expectedTs, result);
    }

    [Fact]
    public void DotNetTypeMapper_MapsNullable()
    {
        var result = DotNetTypeMapper.MapToTypeScript(typeof(int?));
        Assert.Equal("number | null", result);
    }

    [Fact]
    public void DotNetTypeMapper_MapsArray()
    {
        var result = DotNetTypeMapper.MapToTypeScript(typeof(string[]));
        Assert.Equal("string[]", result);
    }

    [Fact]
    public void DotNetTypeMapper_MapsList()
    {
        var result = DotNetTypeMapper.MapToTypeScript(typeof(List<int>));
        Assert.Equal("number[]", result);
    }

    [Fact]
    public void DotNetTypeMapper_MapsDictionary()
    {
        var result = DotNetTypeMapper.MapToTypeScript(typeof(Dictionary<string, int>));
        Assert.Equal("Map<string, number>", result);
    }

    [Fact]
    public void DotNetTypeMapper_MapsHashSet()
    {
        var result = DotNetTypeMapper.MapToTypeScript(typeof(HashSet<string>));
        Assert.Equal("Set<string>", result);
    }

    [Fact]
    public void DotNetTypeMapper_MapsTask()
    {
        var result = DotNetTypeMapper.MapToTypeScript(typeof(System.Threading.Tasks.Task));
        Assert.Equal("Promise<void>", result);
    }

    [Fact]
    public void DotNetTypeMapper_MapsTaskOfT()
    {
        var result = DotNetTypeMapper.MapToTypeScript(typeof(System.Threading.Tasks.Task<string>));
        Assert.Equal("Promise<string>", result);
    }

    [Fact]
    public void DotNetTypeMapper_MapsDateTime()
    {
        var result = DotNetTypeMapper.MapToTypeScript(typeof(DateTime));
        Assert.Equal("Date", result);
    }

    #endregion

    #region Naming Convention Tests

    [Theory]
    [InlineData("WriteLine", "writeLine")]
    [InlineData("GetValue", "getValue")]
    [InlineData("ToString", "toString")]
    [InlineData("ID", "iD")]
    [InlineData("already_snake", "already_snake")]
    public void DotNetTypeMapper_ConvertsToCamelCase(string input, string expected)
    {
        var result = DotNetTypeMapper.ToTypeScriptMethodName(input);
        Assert.Equal(expected, result);
    }

    #endregion

    #region Type Inspector Tests

    [Fact]
    public void TypeInspector_ExtractsPublicMethods()
    {
        var inspector = new TypeInspector();
        var metadata = inspector.Inspect(typeof(System.Text.StringBuilder));

        Assert.NotEmpty(metadata.Methods);
        Assert.Contains(metadata.Methods, m => m.Name == "Append");
        Assert.Contains(metadata.Methods, m => m.Name == "ToString");
    }

    [Fact]
    public void TypeInspector_ExtractsConstructors()
    {
        var inspector = new TypeInspector();
        var metadata = inspector.Inspect(typeof(System.Text.StringBuilder));

        Assert.NotEmpty(metadata.Constructors);
        // StringBuilder has a parameterless constructor
        Assert.Contains(metadata.Constructors, c => c.Parameters.Count == 0);
    }

    [Fact]
    public void TypeInspector_ExtractsProperties()
    {
        var inspector = new TypeInspector();
        var metadata = inspector.Inspect(typeof(System.Text.StringBuilder));

        Assert.NotEmpty(metadata.Properties);
        Assert.Contains(metadata.Properties, p => p.Name == "Length");
    }

    [Fact]
    public void TypeInspector_ExtractsStaticMethods()
    {
        var inspector = new TypeInspector();
        var metadata = inspector.Inspect(typeof(Guid));

        Assert.NotEmpty(metadata.StaticMethods);
        Assert.Contains(metadata.StaticMethods, m => m.Name == "NewGuid");
    }

    [Fact]
    public void TypeInspector_ExtractsStaticProperties()
    {
        var inspector = new TypeInspector();
        var metadata = inspector.Inspect(typeof(Console));

        Assert.NotEmpty(metadata.StaticProperties);
        // Console has static properties like In, Out, Error
        Assert.Contains(metadata.StaticProperties, p => p.Name == "Out" || p.Name == "In" || p.Name == "Error");
    }

    [Fact]
    public void TypeInspector_IdentifiesStaticClass()
    {
        var inspector = new TypeInspector();
        var metadata = inspector.Inspect(typeof(Console));

        Assert.True(metadata.IsStatic);
    }

    [Fact]
    public void TypeInspector_IdentifiesAbstractClass()
    {
        var inspector = new TypeInspector();
        var metadata = inspector.Inspect(typeof(System.IO.Stream));

        Assert.True(metadata.IsAbstract);
    }

    [Fact]
    public void TypeInspector_ExtractsEnumMembers()
    {
        var inspector = new TypeInspector();
        var metadata = inspector.Inspect(typeof(DayOfWeek));

        Assert.True(metadata.IsEnum);
        Assert.NotEmpty(metadata.EnumMembers);
        Assert.Contains(metadata.EnumMembers, e => e.Name == "Sunday");
        Assert.Contains(metadata.EnumMembers, e => e.Name == "Monday");
    }

    #endregion

    #region Complex Nested Generics Tests

    [Fact]
    public void DotNetTypeMapper_MapsDictionaryWithListValue()
    {
        var result = DotNetTypeMapper.MapToTypeScript(typeof(Dictionary<string, List<int>>));
        Assert.Equal("Map<string, number[]>", result);
    }

    [Fact]
    public void DotNetTypeMapper_MapsTaskOfDictionary()
    {
        var result = DotNetTypeMapper.MapToTypeScript(typeof(System.Threading.Tasks.Task<Dictionary<string, int>>));
        Assert.Equal("Promise<Map<string, number>>", result);
    }

    [Fact]
    public void DotNetTypeMapper_MapsListOfDictionary()
    {
        var result = DotNetTypeMapper.MapToTypeScript(typeof(List<Dictionary<string, bool>>));
        Assert.Equal("Map<string, boolean>[]", result);
    }

    [Fact]
    public void DotNetTypeMapper_MapsTripleNesting()
    {
        var result = DotNetTypeMapper.MapToTypeScript(typeof(System.Threading.Tasks.Task<Dictionary<string, List<int>>>));
        Assert.Equal("Promise<Map<string, number[]>>", result);
    }

    [Fact]
    public void DotNetTypeMapper_MapsHashSetOfList()
    {
        var result = DotNetTypeMapper.MapToTypeScript(typeof(HashSet<List<string>>));
        Assert.Equal("Set<string[]>", result);
    }

    [Fact]
    public void DotNetTypeMapper_MapsListWithNullable()
    {
        var result = DotNetTypeMapper.MapToTypeScript(typeof(List<int?>));
        Assert.Equal("(number | null)[]", result);
    }

    [Fact]
    public void DotNetTypeMapper_MapsDictionaryWithNullableValue()
    {
        var result = DotNetTypeMapper.MapToTypeScript(typeof(Dictionary<string, int?>));
        Assert.Equal("Map<string, number | null>", result);
    }

    [Fact]
    public void DotNetTypeMapper_MapsNestedTuple()
    {
        var result = DotNetTypeMapper.MapToTypeScript(typeof(Tuple<string, List<int>>));
        Assert.Equal("[string, number[]]", result);
    }

    [Fact]
    public void DotNetTypeMapper_MapsValueTaskOfDictionary()
    {
        var result = DotNetTypeMapper.MapToTypeScript(typeof(System.Threading.Tasks.ValueTask<Dictionary<int, string>>));
        Assert.Equal("Promise<Map<number, string>>", result);
    }

    [Fact]
    public void DotNetTypeMapper_MapsDictionaryOfLists()
    {
        var result = DotNetTypeMapper.MapToTypeScript(typeof(Dictionary<string, List<string>>));
        Assert.Equal("Map<string, string[]>", result);
    }

    #endregion

    #region Obsolete Attribute Tests

    [Fact]
    public void TypeInspector_ExtractsObsoleteMethod_NoMessage()
    {
        var inspector = new TypeInspector();
        var metadata = inspector.Inspect(typeof(ObsoleteTestFixture));

        var method = metadata.Methods.FirstOrDefault(m => m.Name == "ObsoleteMethodNoMessage");
        Assert.NotNull(method);
        Assert.NotNull(method.Obsolete);
        Assert.Null(method.Obsolete.Message);
        Assert.False(method.Obsolete.IsError);
    }

    [Fact]
    public void TypeInspector_ExtractsObsoleteMethod_WithMessage()
    {
        var inspector = new TypeInspector();
        var metadata = inspector.Inspect(typeof(ObsoleteTestFixture));

        var method = metadata.Methods.FirstOrDefault(m => m.Name == "ObsoleteMethodWithMessage");
        Assert.NotNull(method);
        Assert.NotNull(method.Obsolete);
        Assert.Equal("Use NewMethod instead", method.Obsolete.Message);
        Assert.False(method.Obsolete.IsError);
    }

    [Fact]
    public void TypeInspector_ExtractsObsoleteMethod_IsError()
    {
        var inspector = new TypeInspector();
        var metadata = inspector.Inspect(typeof(ObsoleteTestFixture));

        var method = metadata.Methods.FirstOrDefault(m => m.Name == "ObsoleteMethodError");
        Assert.NotNull(method);
        Assert.NotNull(method.Obsolete);
        Assert.Equal("This will be removed", method.Obsolete.Message);
        Assert.True(method.Obsolete.IsError);
    }

    [Fact]
    public void TypeInspector_ExtractsObsoleteProperty()
    {
        var inspector = new TypeInspector();
        var metadata = inspector.Inspect(typeof(ObsoleteTestFixture));

        var prop = metadata.Properties.FirstOrDefault(p => p.Name == "ObsoleteProperty");
        Assert.NotNull(prop);
        Assert.NotNull(prop.Obsolete);
        Assert.Equal("Use NewProperty instead", prop.Obsolete.Message);
    }

    [Fact]
    public void TypeInspector_ExtractsObsoleteClass()
    {
        var inspector = new TypeInspector();
#pragma warning disable CS0618 // Deliberately testing obsolete class extraction
        var metadata = inspector.Inspect(typeof(ObsoleteClass));
#pragma warning restore CS0618

        Assert.NotNull(metadata.Obsolete);
        Assert.Equal("Use NewClass instead", metadata.Obsolete.Message);
    }

    #endregion

    #region Nested Types Tests

    [Fact]
    public void TypeInspector_DetectsNestedClass()
    {
        var inspector = new TypeInspector();
        var metadata = inspector.Inspect(typeof(OuterClass.NestedClass));

        Assert.True(metadata.IsNested);
        Assert.Equal("OuterClass", metadata.DeclaringTypeName);
        Assert.Equal("NestedClass", metadata.SimpleName);
    }

    [Fact]
    public void TypeInspector_DetectsNestedEnum()
    {
        var inspector = new TypeInspector();
        var metadata = inspector.Inspect(typeof(OuterClass.NestedEnum));

        Assert.True(metadata.IsNested);
        Assert.True(metadata.IsEnum);
        Assert.Equal("OuterClass", metadata.DeclaringTypeName);
    }

    [Fact]
    public void TypeInspector_DetectsNonNestedClass()
    {
        var inspector = new TypeInspector();
        var metadata = inspector.Inspect(typeof(OuterClass));

        Assert.False(metadata.IsNested);
        Assert.Null(metadata.DeclaringTypeName);
    }

    #endregion
}

#region Test Fixtures for Obsolete Attribute Tests

public class ObsoleteTestFixture
{
    [Obsolete]
    public void ObsoleteMethodNoMessage() { }

    [Obsolete("Use NewMethod instead")]
    public void ObsoleteMethodWithMessage() { }

    [Obsolete("This will be removed", true)]
    public void ObsoleteMethodError() { }

    [Obsolete("Use NewProperty instead")]
    public string? ObsoleteProperty { get; set; }

    public void NotObsoleteMethod() { }
}

[Obsolete("Use NewClass instead")]
public class ObsoleteClass
{
    public string? Value { get; set; }
}

#endregion

#region Test Fixtures for Nested Types Tests

public class OuterClass
{
    public string? Name { get; set; }

    public class NestedClass
    {
        public string? Value { get; set; }
    }

    public enum NestedEnum
    {
        A,
        B,
        C
    }
}

#endregion
