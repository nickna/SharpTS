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

    #region TypeScript Emitter Tests

    [Fact]
    public void TypeScriptEmitter_EmitsClassDeclaration()
    {
        var metadata = new TypeMetadata(
            "Test.MyClass",
            "MyClass",
            IsStatic: false,
            IsAbstract: false,
            IsInterface: false,
            IsEnum: false,
            Methods: [],
            StaticMethods: [],
            Properties: [],
            StaticProperties: [],
            Constructors: [],
            EnumMembers: []
        );

        var emitter = new TypeScriptEmitter();
        var result = emitter.Emit(metadata);

        Assert.Contains("@DotNetType(\"Test.MyClass\")", result);
        Assert.Contains("export declare class MyClass", result);
    }

    [Fact]
    public void TypeScriptEmitter_EmitsConstructor()
    {
        var metadata = new TypeMetadata(
            "Test.MyClass",
            "MyClass",
            IsStatic: false,
            IsAbstract: false,
            IsInterface: false,
            IsEnum: false,
            Methods: [],
            StaticMethods: [],
            Properties: [],
            StaticProperties: [],
            Constructors: [
                new ConstructorMetadata([
                    new ParameterMetadata("name", typeof(string), false, null)
                ])
            ],
            EnumMembers: []
        );

        var emitter = new TypeScriptEmitter();
        var result = emitter.Emit(metadata);

        Assert.Contains("constructor(name: string);", result);
    }

    [Fact]
    public void TypeScriptEmitter_EmitsMethod()
    {
        var metadata = new TypeMetadata(
            "Test.MyClass",
            "MyClass",
            IsStatic: false,
            IsAbstract: false,
            IsInterface: false,
            IsEnum: false,
            Methods: [
                new MethodMetadata("GetValue", "getValue", typeof(int), [])
            ],
            StaticMethods: [],
            Properties: [],
            StaticProperties: [],
            Constructors: [],
            EnumMembers: []
        );

        var emitter = new TypeScriptEmitter();
        var result = emitter.Emit(metadata);

        Assert.Contains("getValue(): number;", result);
    }

    [Fact]
    public void TypeScriptEmitter_EmitsStaticMethod()
    {
        var metadata = new TypeMetadata(
            "Test.MyClass",
            "MyClass",
            IsStatic: false,
            IsAbstract: false,
            IsInterface: false,
            IsEnum: false,
            Methods: [],
            StaticMethods: [
                new MethodMetadata("Create", "create", typeof(string), [])
            ],
            Properties: [],
            StaticProperties: [],
            Constructors: [],
            EnumMembers: []
        );

        var emitter = new TypeScriptEmitter();
        var result = emitter.Emit(metadata);

        Assert.Contains("static create(): string;", result);
    }

    [Fact]
    public void TypeScriptEmitter_EmitsProperty()
    {
        var metadata = new TypeMetadata(
            "Test.MyClass",
            "MyClass",
            IsStatic: false,
            IsAbstract: false,
            IsInterface: false,
            IsEnum: false,
            Methods: [],
            StaticMethods: [],
            Properties: [
                new PropertyMetadata("Length", "length", typeof(int), true, false)
            ],
            StaticProperties: [],
            Constructors: [],
            EnumMembers: []
        );

        var emitter = new TypeScriptEmitter();
        var result = emitter.Emit(metadata);

        Assert.Contains("readonly length: number;", result);
    }

    [Fact]
    public void TypeScriptEmitter_EmitsEnum()
    {
        var metadata = new TypeMetadata(
            "Test.MyEnum",
            "MyEnum",
            IsStatic: false,
            IsAbstract: false,
            IsInterface: false,
            IsEnum: true,
            Methods: [],
            StaticMethods: [],
            Properties: [],
            StaticProperties: [],
            Constructors: [],
            EnumMembers: [
                new EnumMemberMetadata("First", 0L),
                new EnumMemberMetadata("Second", 1L)
            ]
        );

        var emitter = new TypeScriptEmitter();
        var result = emitter.Emit(metadata);

        Assert.Contains("export declare enum MyEnum", result);
        Assert.Contains("First = 0", result);
        Assert.Contains("Second = 1", result);
    }

    [Fact]
    public void TypeScriptEmitter_EmitsAbstractClass()
    {
        var metadata = new TypeMetadata(
            "Test.MyAbstractClass",
            "MyAbstractClass",
            IsStatic: false,
            IsAbstract: true,
            IsInterface: false,
            IsEnum: false,
            Methods: [],
            StaticMethods: [],
            Properties: [],
            StaticProperties: [],
            Constructors: [],
            EnumMembers: []
        );

        var emitter = new TypeScriptEmitter();
        var result = emitter.Emit(metadata);

        Assert.Contains("export declare abstract class MyAbstractClass", result);
    }

    #endregion

    #region Declaration Generator Integration Tests

    [Fact]
    public void DeclarationGenerator_GeneratesForStringBuilder()
    {
        var generator = new DeclarationGenerator();
        var result = generator.GenerateForType("System.Text.StringBuilder");

        Assert.Contains("@DotNetType(\"System.Text.StringBuilder\")", result);
        Assert.Contains("export declare class StringBuilder", result);
        Assert.Contains("constructor", result);
        Assert.Contains("append", result);
        Assert.Contains("toString", result);
    }

    [Fact]
    public void DeclarationGenerator_GeneratesForGuid()
    {
        var generator = new DeclarationGenerator();
        var result = generator.GenerateForType("System.Guid");

        Assert.Contains("@DotNetType(\"System.Guid\")", result);
        Assert.Contains("export declare class Guid", result);
        Assert.Contains("static newGuid", result);
        // Guid has various static methods and properties
        Assert.Contains("toString", result);
    }

    [Fact]
    public void DeclarationGenerator_GeneratesForConsole()
    {
        var generator = new DeclarationGenerator();
        var result = generator.GenerateForType("System.Console");

        Assert.Contains("@DotNetType(\"System.Console\")", result);
        Assert.Contains("export declare class Console", result);
        Assert.Contains("static writeLine", result);
        Assert.Contains("static readLine", result);
    }

    [Fact]
    public void DeclarationGenerator_GeneratesForDateTime()
    {
        var generator = new DeclarationGenerator();
        var result = generator.GenerateForType("System.DateTime");

        Assert.Contains("@DotNetType(\"System.DateTime\")", result);
        Assert.Contains("export declare class DateTime", result);
        Assert.Contains("static readonly now", result);
    }

    [Fact]
    public void DeclarationGenerator_GeneratesForTimeSpan()
    {
        var generator = new DeclarationGenerator();
        var result = generator.GenerateForType("System.TimeSpan");

        Assert.Contains("@DotNetType(\"System.TimeSpan\")", result);
        Assert.Contains("export declare class TimeSpan", result);
        Assert.Contains("static fromSeconds", result);
        Assert.Contains("static fromMinutes", result);
    }

    [Fact]
    public void DeclarationGenerator_ThrowsForUnknownType()
    {
        var generator = new DeclarationGenerator();
        Assert.Throws<ArgumentException>(() => generator.GenerateForType("NonExistent.Type.That.Does.Not.Exist"));
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

    #region Optional Parameter Tests

    [Fact]
    public void TypeScriptEmitter_EmitsOptionalParameter()
    {
        var metadata = new TypeMetadata(
            "Test.MyClass",
            "MyClass",
            IsStatic: false,
            IsAbstract: false,
            IsInterface: false,
            IsEnum: false,
            Methods: [
                new MethodMetadata("DoSomething", "doSomething", typeof(void), [
                    new ParameterMetadata("required", typeof(string), false, null),
                    new ParameterMetadata("optional", typeof(int), true, 42)
                ])
            ],
            StaticMethods: [],
            Properties: [],
            StaticProperties: [],
            Constructors: [],
            EnumMembers: []
        );

        var emitter = new TypeScriptEmitter();
        var result = emitter.Emit(metadata);

        Assert.Contains("doSomething(required: string, optional?: number): void;", result);
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

    [Fact]
    public void TypeScriptEmitter_EmitsDeprecatedMethod_NoMessage()
    {
        var metadata = new TypeMetadata(
            "Test.MyClass",
            "MyClass",
            IsStatic: false,
            IsAbstract: false,
            IsInterface: false,
            IsEnum: false,
            Methods: [
                new MethodMetadata("OldMethod", "oldMethod", typeof(void), [],
                    Obsolete: new ObsoleteMetadata(null, false))
            ],
            StaticMethods: [],
            Properties: [],
            StaticProperties: [],
            Constructors: [],
            EnumMembers: []
        );

        var emitter = new TypeScriptEmitter();
        var result = emitter.Emit(metadata);

        Assert.Contains("/** @deprecated */", result);
        Assert.Contains("oldMethod(): void;", result);
    }

    [Fact]
    public void TypeScriptEmitter_EmitsDeprecatedMethod_WithMessage()
    {
        var metadata = new TypeMetadata(
            "Test.MyClass",
            "MyClass",
            IsStatic: false,
            IsAbstract: false,
            IsInterface: false,
            IsEnum: false,
            Methods: [
                new MethodMetadata("OldMethod", "oldMethod", typeof(void), [],
                    Obsolete: new ObsoleteMetadata("Use newMethod instead", false))
            ],
            StaticMethods: [],
            Properties: [],
            StaticProperties: [],
            Constructors: [],
            EnumMembers: []
        );

        var emitter = new TypeScriptEmitter();
        var result = emitter.Emit(metadata);

        Assert.Contains("/** @deprecated Use newMethod instead */", result);
        Assert.Contains("oldMethod(): void;", result);
    }

    [Fact]
    public void TypeScriptEmitter_EmitsDeprecatedProperty()
    {
        var metadata = new TypeMetadata(
            "Test.MyClass",
            "MyClass",
            IsStatic: false,
            IsAbstract: false,
            IsInterface: false,
            IsEnum: false,
            Methods: [],
            StaticMethods: [],
            Properties: [
                new PropertyMetadata("OldProp", "oldProp", typeof(string), true, false,
                    Obsolete: new ObsoleteMetadata("Use newProp instead", false))
            ],
            StaticProperties: [],
            Constructors: [],
            EnumMembers: []
        );

        var emitter = new TypeScriptEmitter();
        var result = emitter.Emit(metadata);

        Assert.Contains("/** @deprecated Use newProp instead */", result);
        Assert.Contains("readonly oldProp: string;", result);
    }

    [Fact]
    public void TypeScriptEmitter_EmitsDeprecatedClass()
    {
        var metadata = new TypeMetadata(
            "Test.OldClass",
            "OldClass",
            IsStatic: false,
            IsAbstract: false,
            IsInterface: false,
            IsEnum: false,
            Methods: [],
            StaticMethods: [],
            Properties: [],
            StaticProperties: [],
            Constructors: [],
            EnumMembers: [],
            Obsolete: new ObsoleteMetadata("Use NewClass instead", false)
        );

        var emitter = new TypeScriptEmitter();
        var result = emitter.Emit(metadata);

        Assert.Contains("/** @deprecated Use NewClass instead */", result);
        Assert.Contains("@DotNetType(\"Test.OldClass\")", result);
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

    [Fact]
    public void TypeScriptEmitter_EmitsNestedTypesInNamespace()
    {
        var outerType = new TypeMetadata(
            "Test.OuterClass",
            "OuterClass",
            IsStatic: false,
            IsAbstract: false,
            IsInterface: false,
            IsEnum: false,
            Methods: [],
            StaticMethods: [],
            Properties: [
                new PropertyMetadata("Name", "name", typeof(string), true, true)
            ],
            StaticProperties: [],
            Constructors: [],
            EnumMembers: []
        );

        var nestedType = new TypeMetadata(
            "Test.OuterClass+NestedClass",
            "NestedClass",
            IsStatic: false,
            IsAbstract: false,
            IsInterface: false,
            IsEnum: false,
            Methods: [],
            StaticMethods: [],
            Properties: [
                new PropertyMetadata("Value", "value", typeof(int), true, true)
            ],
            StaticProperties: [],
            Constructors: [],
            EnumMembers: [],
            IsNested: true,
            DeclaringTypeName: "OuterClass"
        );

        var emitter = new TypeScriptEmitter();
        var result = emitter.EmitAll([outerType, nestedType], groupNestedTypes: true);

        Assert.Contains("export declare class OuterClass", result);
        Assert.Contains("export namespace OuterClass", result);
        Assert.Contains("export declare class NestedClass", result);
    }

    [Fact]
    public void TypeScriptEmitter_EmitsNestedEnumInNamespace()
    {
        var outerType = new TypeMetadata(
            "Test.OuterClass",
            "OuterClass",
            IsStatic: false,
            IsAbstract: false,
            IsInterface: false,
            IsEnum: false,
            Methods: [],
            StaticMethods: [],
            Properties: [],
            StaticProperties: [],
            Constructors: [],
            EnumMembers: []
        );

        var nestedEnum = new TypeMetadata(
            "Test.OuterClass+NestedEnum",
            "NestedEnum",
            IsStatic: false,
            IsAbstract: false,
            IsInterface: false,
            IsEnum: true,
            Methods: [],
            StaticMethods: [],
            Properties: [],
            StaticProperties: [],
            Constructors: [],
            EnumMembers: [
                new EnumMemberMetadata("A", 0L),
                new EnumMemberMetadata("B", 1L)
            ],
            IsNested: true,
            DeclaringTypeName: "OuterClass"
        );

        var emitter = new TypeScriptEmitter();
        var result = emitter.EmitAll([outerType, nestedEnum], groupNestedTypes: true);

        Assert.Contains("export namespace OuterClass", result);
        Assert.Contains("export declare enum NestedEnum", result);
        Assert.Contains("A = 0", result);
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
