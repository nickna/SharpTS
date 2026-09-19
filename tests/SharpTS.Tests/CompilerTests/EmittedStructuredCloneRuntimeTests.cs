using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedStructuredCloneRuntimeTests
{
    private static IEnumerable<PropertyInfo> Handles => typeof(EmittedStructuredCloneRuntime).GetProperties()
        .Where(property => property.PropertyType != typeof(bool));

    public static IEnumerable<object[]> HandleNames => Handles.Select(property => new object[] { property.Name });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void MissingDeclarationRejectsReadsAndCompletionThenAllowsRepair(string missingHandle)
    {
        var clone = CreateDeclarations(missingHandle);
        var property = typeof(EmittedStructuredCloneRuntime).GetProperty(missingHandle)!;
        var error = Assert.Throws<TargetInvocationException>(() => property.GetValue(clone));
        Assert.Contains($"'{missingHandle}'", Assert.IsType<InvalidOperationException>(error.InnerException).Message);
        Assert.Contains($"'{missingHandle}'", Assert.Throws<InvalidOperationException>(clone.CompleteEmission).Message);
        Assert.False(clone.IsComplete);
        var nullError = Assert.Throws<TargetInvocationException>(() => property.SetValue(clone, null));
        Assert.IsType<ArgumentNullException>(nullError.InnerException);
        property.SetValue(clone, property.GetValue(CreateDeclarations()));
        clone.CompleteEmission();
        AssertFrozen(clone);
    }

    [Fact]
    public void RequiredOwnerExistsBeforeEmissionAndCannotBeReplaced()
    {
        var first = new EmittedRuntime();
        var second = new EmittedRuntime();
        Assert.NotSame(first.StructuredClone, second.StructuredClone);
        Assert.False(first.StructuredClone.IsComplete);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.StructuredClone))!.SetMethod);
        Assert.Throws<InvalidOperationException>(() => first.StructuredClone.Clone);
        Assert.Throws<InvalidOperationException>(() => first.StructuredClone.ErrorType);
        Assert.Throws<InvalidOperationException>(() => first.StructuredClone.ErrorCtor);
    }

    [Fact]
    public void DedicatedExceptionIsUsableBeforeTheCloneWrapperIsDeclared()
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("clone_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var clone = new EmittedStructuredCloneRuntime();
        typeof(RuntimeEmitter).GetMethod("EmitTSDataCloneErrorType", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(new RuntimeEmitter(TypeProvider.Runtime), [module, clone]);
        Assert.True(clone.ErrorType.IsCreated());
        Assert.Same(clone.ErrorType, clone.ErrorCtor.DeclaringType);
        Assert.True(clone.ErrorCtor.GetILGenerator().ILOffset > 0);
        Assert.False(clone.IsComplete);
        Assert.Contains("Clone", Assert.Throws<InvalidOperationException>(clone.CompleteEmission).Message);
        using var bytes = new MemoryStream();
        assembly.Save(bytes);
        bytes.Position = 0;
        Verify(bytes);
        var type = Assembly.Load(bytes.ToArray()).GetType("$DataCloneError")!;
        Assert.Equal(typeof(Exception), type.BaseType);
        Assert.Equal("DataCloneError: detail", Assert.IsAssignableFrom<Exception>(Activator.CreateInstance(type, ["detail"])).Message);
    }

    [Theory]
    [InlineData("const value=1;", false)]
    [InlineData("const value=1;", true)]
    [InlineData("structuredClone({value:1});", false)]
    [InlineData("new Uint8Array(2);", false)]
    [InlineData("new SharedArrayBuffer(4);", false)]
    [InlineData("Buffer.from('value');", false)]
    [InlineData("new Date(0);", false)]
    [InlineData("new RegExp('value','g');", false)]
    [InlineData("new MessageChannel();", false)]
    [InlineData("new BroadcastChannel('name');", false)]
    [InlineData(null, false)]
    [InlineData(null, true)]
    public void RequiredDeclarationsAndOptionalPeersPreserveSavedSignaturesAndDependencies(string? source, bool hosted)
    {
        var runtime = EmitRuntime(source, hosted);
        AssertFrozen(runtime.StructuredClone);
        using var bytes = Save(runtime);
        using var pe = new PEReader(bytes);
        var reader = pe.GetMetadataReader();
        var names = reader.TypeDefinitions.Select(handle => reader.GetString(reader.GetTypeDefinition(handle).Name)).ToArray();
        Assert.Contains("$DataCloneError", names);
        var features = source is null ? RuntimeFeatureSet.EmitEverything() : Detect(source);
        Assert.Equal(features.HasAnyTypedArray, names.Contains("$ArrayBuffer"));
        Assert.Equal(features.UsesBuffer, names.Contains("$Buffer"));
        Assert.Equal(features.UsesDate, names.Contains("$TSDate"));
        Assert.Equal(features.UsesRegExp, names.Contains("$RegExp"));
        var references = reader.AssemblyReferences.Select(handle => reader.GetString(reader.GetAssemblyReference(handle).Name)).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        var assembly = Assembly.Load(bytes.ToArray());
        var type = assembly.GetType("$Runtime")!;
        var method = type.GetMethod("StructuredClone")!;
        Assert.True(method.IsPublic && method.IsStatic);
        Assert.Equal(typeof(object), method.ReturnType);
        Assert.Equal(new[] { typeof(object), typeof(object) }, method.GetParameters().Select(parameter => parameter.ParameterType));
        var core = type.GetMethod("StructuredCloneCore", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.True(core.IsPrivate);
        Assert.Equal(typeof(object), Assert.Single(core.GetParameters()).ParameterType);
        Assert.Equal(typeof(object), core.ReturnType);
        var errorType = assembly.GetType("$DataCloneError")!;
        Assert.Equal(typeof(Exception), errorType.BaseType);
        Assert.NotNull(errorType.GetConstructor([typeof(string)]));
        var primitive = new object();
        Assert.Same(primitive, method.Invoke(null, [primitive, new object()]));
    }

    [Fact]
    public void SavedCloneRecursivelyCopiesContainerDataAndPreservesForeignValues()
    {
        var runtime = EmitRuntime("structuredClone({value:1});", false);
        using var bytes = Save(runtime);
        Verify(bytes);
        var method = Assembly.Load(bytes.ToArray()).GetType("$Runtime")!.GetMethod("StructuredClone")!;
        object? Clone(object? value) => method.Invoke(null, [value, null]);
        var foreign = new object();
        var task = Task.FromResult<object?>(7d);
        foreach (var value in new object?[] { null, 1d, "value", true, new System.Numerics.BigInteger(12), foreign, task })
            Assert.Same(value, Clone(value));
        var source = new Dictionary<string, object?> { ["items"] = new List<object?> { new Dictionary<string, object?> { ["value"] = 7d }, foreign } };
        var copy = Assert.IsType<Dictionary<string, object?>>(Clone(source));
        Assert.NotSame(source, copy);
        var originalItems = Assert.IsType<List<object?>>(source["items"]);
        var items = Assert.IsType<List<object?>>(copy["items"]);
        Assert.NotSame(originalItems, items);
        var originalItem = Assert.IsType<Dictionary<string, object?>>(originalItems[0]);
        var item = Assert.IsType<Dictionary<string, object?>>(items[0]);
        Assert.NotSame(originalItem, item);
        originalItem["value"] = 9d;
        Assert.Equal(7d, item["value"]);
        Assert.Same(foreign, items[1]);
        var map = new Dictionary<object, object?> { [originalItem] = originalItems };
        var mapCopy = Assert.IsType<Dictionary<object, object?>>(Clone(map));
        var pair = Assert.Single(mapCopy);
        Assert.NotSame(originalItem, pair.Key);
        Assert.NotSame(originalItems, pair.Value);
        var setCopy = Assert.IsType<HashSet<object?>>(Clone(new HashSet<object?> { originalItem }));
        Assert.NotSame(originalItem, Assert.Single(setCopy));
    }

    [Fact]
    public void OwnPromiseAndNestedOccurrencesThrowTheDedicatedErrorWhileForeignPromisesPassThrough()
    {
        var runtime = EmitRuntime("Promise.resolve(1);", false);
        using var bytes = Save(runtime);
        Verify(bytes);
        var assembly = Assembly.Load(bytes.ToArray());
        var clone = assembly.GetType("$Runtime")!.GetMethod("StructuredClone")!;
        var task = Task.FromResult<object?>(7d);
        var promise = Activator.CreateInstance(assembly.GetType("$Promise")!, [task])!;
        foreach (var value in new object[] { promise, new List<object?> { promise }, new Dictionary<string, object?> { ["value"] = promise }, new Dictionary<object, object?> { [promise] = 7d }, new HashSet<object?> { promise } })
        {
            var error = Assert.Throws<TargetInvocationException>(() => clone.Invoke(null, [value, null]));
            Assert.Equal(assembly.GetType("$DataCloneError"), error.InnerException!.GetType());
            Assert.Equal("DataCloneError: Cannot clone value of type $Promise", error.InnerException.Message);
        }
        // An optimized Promise.resolve value can be a BCL Task; foreign values intentionally pass through.
        Assert.Same(task, clone.Invoke(null, [task, null]));
        using var otherBytes = Save(EmitRuntime("Promise.resolve(1);", false));
        var foreign = Activator.CreateInstance(Assembly.Load(otherBytes.ToArray()).GetType("$Promise")!, [task])!;
        Assert.Same(foreign, clone.Invoke(null, [foreign, null]));
    }

    [Theory]
    [InlineData("Error")]
    [InlineData("TypeError")]
    [InlineData("RangeError")]
    [InlineData("ReferenceError")]
    [InlineData("SyntaxError")]
    [InlineData("URIError")]
    [InlineData("EvalError")]
    public void BuiltInErrorsRetainConcreteNameMessageAndStack(string name)
    {
        var runtime = EmitRuntime("structuredClone(new Error('detail'));", false);
        using var bytes = Save(runtime);
        var assembly = Assembly.Load(bytes.ToArray());
        var type = assembly.GetType("$" + name)!;
        var error = Activator.CreateInstance(type, ["detail"])!;
        type.GetProperty("Stack")!.SetValue(error, "saved stack");
        var copy = assembly.GetType("$Runtime")!.GetMethod("StructuredClone")!.Invoke(null, [error, null])!;
        Assert.NotSame(error, copy);
        Assert.Equal(type, copy.GetType());
        Assert.Equal(name, type.GetProperty("Name")!.GetValue(copy));
        Assert.Equal("detail", type.GetProperty("Message")!.GetValue(copy));
        Assert.Equal("saved stack", type.GetProperty("Stack")!.GetValue(copy));
    }

    [Fact]
    public void ReusingEmitterAcrossFeatureSetsKeepsCloneDeclarationsIndependent()
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var first = EmitRuntime(null, false, emitter);
        var second = EmitRuntime("const value=1;", false, emitter);
        Assert.NotSame(first.StructuredClone, second.StructuredClone);
        foreach (var property in Handles)
            Assert.NotSame(property.GetValue(first.StructuredClone), property.GetValue(second.StructuredClone));
        AssertFrozen(first.StructuredClone);
        AssertFrozen(second.StructuredClone);
        using var bytes = Save(second);
        Verify(bytes);
        Assert.Null(Assembly.Load(bytes.ToArray()).GetType("$TSDate"));
    }

    private static EmittedStructuredCloneRuntime CreateDeclarations(string? omitted = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"clone_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("main").DefineType("CloneDeclarations", TypeAttributes.Public);
        var clone = new EmittedStructuredCloneRuntime();
        foreach (var property in Handles)
        {
            if (property.Name == omitted) continue;
            object member;
            if (property.PropertyType == typeof(TypeBuilder)) member = type;
            else if (property.PropertyType == typeof(ConstructorBuilder)) member = type.DefineDefaultConstructor(MethodAttributes.Public);
            else
            {
                var method = type.DefineMethod(property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
                method.GetILGenerator().Emit(OpCodes.Ret);
                member = method;
            }
            property.SetValue(clone, member);
        }
        return clone;
    }

    private static void AssertFrozen(EmittedStructuredCloneRuntime clone)
    {
        Assert.True(clone.IsComplete);
        Assert.Throws<InvalidOperationException>(clone.CompleteEmission);
        foreach (var property in Handles)
        {
            var value = property.GetValue(clone);
            Assert.NotNull(value);
            Assert.False(property.SetMethod!.IsPublic);
            var error = Assert.Throws<TargetInvocationException>(() => property.SetValue(clone, value));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }

    private static void Verify(MemoryStream bytes)
    {
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
    }

    private static MemoryStream Save(EmittedRuntime runtime)
    {
        var bytes = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeClass.Type.Assembly).Save(bytes);
        bytes.Position = 0;
        return bytes;
    }

    private static RuntimeFeatureSet Detect(string source) => new RuntimeFeatureDetector()
        .Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    private static EmittedRuntime EmitRuntime(string? source, bool hosted, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"structured_clone_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        return source is null ? emitter.EmitAll(module) : emitter.EmitAll(module, Detect(source));
    }
}
