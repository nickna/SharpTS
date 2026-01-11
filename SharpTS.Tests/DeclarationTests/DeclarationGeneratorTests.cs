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
}
