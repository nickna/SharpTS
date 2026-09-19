using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedJsonRuntimeTests
{
    private const BindingFlags StaticMethods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private static PropertyInfo[] Handles(Type type) => type.GetProperties()
        .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType)).ToArray();

    private static IEnumerable<object> Groups(EmittedJsonRuntime owner)
    {
        yield return owner;
        if (owner.Implementation is { } implementation) yield return implementation;
    }

    public static IEnumerable<object[]> HandleNames => new[] { typeof(EmittedJsonRuntime), typeof(EmittedJsonImplementation) }
        .SelectMany(type => Handles(type).Select(property => new object[] { type == typeof(EmittedJsonImplementation), property.Name }));

    public static IEnumerable<object[]> Selections => Enumerable.Range(0, 4)
        .SelectMany(mask => new[] { new object[] { mask, false }, new object[] { mask, true } });

    [Theory]
    [MemberData(nameof(HandleNames))]
    public void MissingDeclarationCanBeRepairedBeforeEitherOwnerIsFrozen(bool implementation, string name)
    {
        var owner = CreateDeclarations(implementation, name);
        object selected = implementation ? owner.RequireImplementation() : owner;
        var property = selected.GetType().GetProperty(name)!;
        var read = Assert.Throws<TargetInvocationException>(() => property.GetValue(selected));
        Assert.Contains($"'{name}'", Assert.IsType<InvalidOperationException>(read.InnerException).Message);
        var write = Assert.Throws<TargetInvocationException>(() => property.SetValue(selected, null));
        Assert.IsType<ArgumentNullException>(write.InnerException);
        Assert.Contains($"'{name}'", Assert.Throws<InvalidOperationException>(owner.CompleteEmission).Message);
        Assert.False(owner.IsComplete);
        Assert.False(owner.RequireImplementation().IsComplete);
        var replacement = CreateDeclarations();
        property.SetValue(selected, property.GetValue(implementation ? replacement.RequireImplementation() : replacement));
        owner.CompleteEmission();
        AssertFrozen(owner);
    }

    [Fact]
    public void RequiredNamespaceAndOptionalImplementationHaveExplicitAvailability()
    {
        var owner = new EmittedRuntime().Json;
        Assert.NotSame(owner, new EmittedRuntime().Json);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.Json))!.SetMethod);
        Assert.Equal(27, HandleNames.Count());
        Assert.Null(owner.Implementation);
        Assert.Throws<InvalidOperationException>(owner.RequireImplementation);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        var declarations = CreateDeclarations();
        owner.SingletonField = declarations.SingletonField;
        owner.SingletonPopulateMethod = declarations.SingletonPopulateMethod;
        owner.CompleteEmission();
        AssertFrozen(owner);
        var selected = new EmittedJsonRuntime();
        selected.BeginImplementationEmission();
        Assert.Throws<InvalidOperationException>(selected.BeginImplementationEmission);
    }

    [Fact]
    public void HttpFeatureDetectionIncludesTheJsonImplementation()
    {
        var features = Detect("import * as http from 'http'; const server = http.createServer((req, res) => res.end('ok'));");
        Assert.True(features.UsesJSON);
    }

    [Theory]
    [MemberData(nameof(Selections))]
    public void SelectedAndAbsentHelpersPreserveSavedContracts(int mask, bool hosted)
    {
        var runtime = EmitRuntime(mask, hosted);
        Assert.Equal((mask & 1) != 0, runtime.Json.Implementation is not null);
        AssertFrozen(runtime.Json);
        var assembly = SaveAndLoad(runtime, hosted);
        AssertSavedContracts(runtime, assembly, [], new string(['k', 'e', 'y']));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullEmissionPreservesSavedContracts(bool hosted)
    {
        var runtime = EmitRuntime(null, hosted);
        AssertFrozen(runtime.Json);
        AssertSavedContracts(runtime, SaveAndLoad(runtime, hosted), [], new string(['k', 'e', 'y']));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterKeepsAllJsonHandlesAndGuestStoresInTheirOwnOutput(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new List<EmittedJsonRuntime>();
        var stores = new List<object>();
        string sharedKey = new(['k', 'e', 'y']);
        foreach (int mask in new[] { 1, 0, 3, 2, 1 })
        {
            var runtime = EmitRuntime(mask, hosted, emitter);
            Assert.DoesNotContain(runtime.Json, owners);
            owners.Add(runtime.Json);
            foreach (var selected in Groups(runtime.Json))
                foreach (var property in Handles(selected.GetType()))
                {
                    var handle = Assert.IsAssignableFrom<MemberInfo>(property.GetValue(selected));
                    Assert.Same(runtime.RuntimeType.Assembly, handle.Module.Assembly);
                }
            AssertFrozen(runtime.Json);
            var assembly = SaveAndLoad(runtime, hosted);
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
                reference.Name!.StartsWith("json_storage_", StringComparison.Ordinal));
            AssertSavedContracts(runtime, assembly, stores, sharedKey);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NamespaceShellDeclarationDoesNotRequireOptionalJsonBodies(bool selected, bool globalJsonFlag)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("json_staged"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var runtime = new EmittedRuntime();
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var features = Detect();
        features.UsesJSON = globalJsonFlag;
        typeof(RuntimeEmitter).GetField("_features", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(emitter, features);
        if (selected) runtime.Json.BeginImplementationEmission();
        typeof(RuntimeEmitter).GetMethod("DefineRuntimeClassPhase1", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(emitter, [module, runtime]);
        // The namespace field and shell are deliberately later than phase one.
        Assert.Throws<InvalidOperationException>(() => runtime.Json.SingletonField);
        Assert.Throws<InvalidOperationException>(() => runtime.Json.SingletonPopulateMethod);
        typeof(RuntimeEmitter).GetMethod("DefineJsonSingletonPopulateShell", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(emitter, [runtime.RuntimeType, runtime.Json]);
        Assert.Same(runtime.RuntimeType, runtime.Json.SingletonPopulateMethod.DeclaringType);
        Assert.Equal(0, runtime.Json.SingletonPopulateMethod.GetILGenerator().ILOffset);
        var caller = runtime.RuntimeType.DefineMethod("ForwardCaller", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        var il = caller.GetILGenerator();
        il.Emit(OpCodes.Call, runtime.Json.SingletonPopulateMethod);
        il.Emit(OpCodes.Ret);
        if (selected)
        {
            Assert.Throws<InvalidOperationException>(() => runtime.Json.RequireImplementation().RawJsonType);
        }
        else Assert.Null(runtime.Json.Implementation);
        Assert.Throws<InvalidOperationException>(runtime.Json.CompleteEmission);
        Assert.False(runtime.Json.IsComplete);
    }

    private static void AssertSavedContracts(EmittedRuntime runtime, Assembly assembly, List<object> stores, string sharedKey)
    {
        var type = assembly.GetType("$Runtime")!;
        var owner = runtime.Json;
        var singleton = Assert.IsType<Dictionary<string, object>>(type.GetField(owner.SingletonField.Name, StaticMethods)!.GetValue(null));
        Call(type, owner.SingletonPopulateMethod.Name);
        Assert.Equal(owner.Implementation is not null, singleton.ContainsKey("parse"));
        Assert.Equal(owner.Implementation is not null, type.GetMethod("JsonParse", StaticMethods) is not null);
        Assert.Equal(owner.Implementation is not null, assembly.GetType("$RawJSON") is not null);
        if (owner.Implementation is not { } json) return;
        var storeField = type.GetField("_jsonShapes", StaticMethods)!;
        Assert.Null(storeField.GetValue(null));
        var lookup = type.GetMethod(json.TryGetShape.Name, StaticMethods)!;
        object?[] query = [sharedKey, null];
        Assert.Equal(false, lookup.Invoke(null, query));
        Assert.Null(query[1]);
        Assert.Null(storeField.GetValue(null));
        object shape = new object[] { "$o", "x", "$n" };
        Call(type, json.RegisterShape.Name, sharedKey, shape);
        Assert.Equal(true, lookup.Invoke(null, query));
        Assert.Same(shape, query[1]);
        query = [new string(sharedKey.ToCharArray()), null];
        Assert.Equal(false, lookup.Invoke(null, query));
        Assert.Null(query[1]);
        query = [new object(), null];
        Assert.Equal(false, lookup.Invoke(null, query));
        var store = storeField.GetValue(null)!;
        Assert.DoesNotContain(stores, old => ReferenceEquals(old, store));
        stores.Add(store);
        object replacement = new object();
        Call(type, json.RegisterShape.Name, sharedKey, replacement);
        query = [sharedKey, null];
        Assert.Equal(true, lookup.Invoke(null, query));
        Assert.Same(replacement, query[1]);

        var value = new Dictionary<string, object> { ["x"] = 1d };
        Assert.Equal("{\"x\":1}", Call(type, json.Stringify.Name, value));
        Assert.Equal("{\"x\":1}", Call(type, json.StringifyFull.Name, value, null, null));
        object parsed = Call(type, json.Parse.Name, "{\"x\":2}")!;
        Assert.Equal(2d, Call(type, "GetProperty", parsed, "x"));
        var raw = Call(type, json.RawJson.Name, "123");
        Assert.Equal(true, Call(type, json.IsRawJson.Name, raw));
        Assert.Equal(false, Call(type, json.IsRawJson.Name, value));
        Assert.Equal("123", Call(type, json.Stringify.Name, raw));
        if (runtime.RegExps.Implementation?.Type is { } regexp)
        {
            var saved = assembly.GetType(regexp.Name)!;
            var instance = Activator.CreateInstance(saved, ["x", ""]);
            Assert.Equal("{}", Call(type, json.Stringify.Name, instance));
            Assert.Equal("{}", Call(type, json.StringifyFull.Name, instance, null, null));
        }
        Assert.Equal(typeof(object), type.GetMethod(json.RawJson.Name, StaticMethods)!.ReturnType);
        Assert.Same(assembly.GetType(json.RawJsonType.Name), assembly.GetType(json.RawJsonTextGetter.DeclaringType!.Name));
        singleton["parse"] = "replacement";
        Call(type, owner.SingletonPopulateMethod.Name);
        Assert.Equal("replacement", Call(type, "GetProperty", singleton, "parse"));
        Assert.True(singleton.Remove("parse"));
        Call(type, owner.SingletonPopulateMethod.Name);
        Assert.Equal("$Undefined", Call(type, "GetProperty", singleton, "parse")!.GetType().Name);
    }

    private static EmittedJsonRuntime CreateDeclarations(bool implementationOmission = false, string? omission = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"json_declarations_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        var type = module.DefineType("JsonDeclarations");
        var owner = new EmittedJsonRuntime();
        owner.BeginImplementationEmission();
        foreach (object selected in Groups(owner))
            foreach (var property in Handles(selected.GetType()))
            {
                if ((selected is EmittedJsonImplementation) == implementationOmission && property.Name == omission) continue;
                object value = property.PropertyType == typeof(FieldBuilder)
                    ? type.DefineField(property.Name, typeof(object), FieldAttributes.Public | FieldAttributes.Static)
                    : property.PropertyType == typeof(TypeBuilder) ? module.DefineType(property.Name)
                    : property.PropertyType == typeof(ConstructorBuilder)
                        ? type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes)
                        : type.DefineMethod(property.Name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
                property.SetValue(selected, value);
            }
        return owner;
    }

    private static void AssertFrozen(EmittedJsonRuntime owner)
    {
        Assert.True(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.Throws<InvalidOperationException>(owner.BeginImplementationEmission);
        foreach (object selected in Groups(owner))
        {
            Assert.Equal(true, selected.GetType().GetProperty("IsComplete")!.GetValue(selected));
            foreach (var property in Handles(selected.GetType()))
            {
                var value = property.GetValue(selected);
                Assert.NotNull(value);
                Assert.False(property.SetMethod!.IsPublic);
                var write = Assert.Throws<TargetInvocationException>(() => property.SetValue(selected, value));
                Assert.IsType<InvalidOperationException>(write.InnerException);
            }
        }
    }

    private static RuntimeFeatureSet Detect(string source = "const value=1;") => new RuntimeFeatureDetector()
        .Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());

    private static EmittedRuntime EmitRuntime(int? mask, bool hosted = false, RuntimeEmitter? emitter = null)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName($"json_storage_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("main");
        emitter ??= new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        if (mask is null) return emitter.EmitAll(module);
        string source = (mask & 1) != 0 ? "console.log(JSON.stringify({x:1}));" : "const value=1;";
        if ((mask & 2) != 0) source += " console.log(/x/.test('x'));";
        return emitter.EmitAll(module, Detect(source));
    }

    private static object? Call(Type type, string name, params object?[] arguments) => type.GetMethod(name, StaticMethods)!.Invoke(null, arguments);

    private static Assembly SaveAndLoad(EmittedRuntime runtime, bool hosted)
    {
        using var bytes = new MemoryStream();
        ((PersistedAssemblyBuilder)runtime.RuntimeType.Assembly).Save(bytes);
        bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var assembly = Assembly.Load(bytes.ToArray());
        var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
        Assert.DoesNotContain("SharpTS", references);
        Assert.Equal(hosted, references.Contains("SharpTS.Hosting.Abstractions"));
        return assembly;
    }
}
