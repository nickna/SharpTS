using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedDynamicConstructionRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static PropertyInfo[] Properties => typeof(EmittedDynamicConstructionRuntime).GetProperties().Where(p => p.PropertyType == typeof(MethodBuilder)).ToArray();
    public static IEnumerable<object[]> Declarations => Properties.Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(Declarations))]
    public void MissingDeclarationCanBeRepairedAndCompletionFreezesMetadata(string missing)
    {
        var owner = new EmittedDynamicConstructionRuntime(); var handles = NewHandles();
        var property = Properties.Single(p => p.Name == missing);
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        foreach (var other in Properties.Where(p => p.Name != missing)) other.SetValue(owner, handles[other.Name]);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        property.SetValue(owner, handles[missing]); Assert.Same(handles[missing], property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, handles[missing]));
        owner.MarkFunctionBodyEmitted(); owner.CompleteEmission(); Assert.True(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.Throws<InvalidOperationException>(owner.MarkFunctionBodyEmitted);
        foreach (var item in Properties) Expect<InvalidOperationException>(() => item.SetValue(owner, handles[item.Name]));
    }

    [Fact]
    public void FunctionShellCannotCompleteBeforeItsBody()
    {
        var owner = new EmittedDynamicConstructionRuntime();
        Assert.Throws<InvalidOperationException>(owner.MarkFunctionBodyEmitted);
        var handles = NewHandles();
        foreach (var item in Properties) item.SetValue(owner, handles[item.Name]);
        Assert.Same(handles["Function"], owner.Function);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        owner.MarkFunctionBodyEmitted(); Assert.Throws<InvalidOperationException>(owner.MarkFunctionBodyEmitted);
        owner.CompleteEmission(); Assert.True(owner.IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterPreservesConstructionAndRestoresAmbientReceiver(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted); var owners = new HashSet<object>();
        foreach (string code in new[] { "const n=1;", "const r=new RegExp('a','g');function F(){}const c:any=F;new c();", "const n=2;" })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(code));
            var owner = runtime.DynamicConstruction; Assert.True(owners.Add(owner)); Assert.True(owner.IsComplete);
            foreach (var property in Properties) Assert.Same(builder, ((MethodInfo)property.GetValue(owner)!).Module.Assembly);
            var loaded = SaveVerifyLoad(builder); var type = loaded.GetType("$Runtime")!;
            var function = loaded.GetType(runtime.FunctionValues.Type.Name)!;
            var current = function.GetField(runtime.FunctionValues.CurrentThisField.Name, Members)!;
            Assert.NotNull(current.GetCustomAttribute<ThreadStaticAttribute>());
            var previous = new object(); current.SetValue(null, previous);
            object Wrap(string name) => Activator.CreateInstance(function, [null, typeof(EmittedDynamicConstructionRuntimeTests).GetMethod(name)!])!;
            object? Construct(string name, object? target, params object[] args) => type.GetMethod(name)!.Invoke(null, [target, args]);
            foreach (string name in new[] { owner.Function.Name, owner.Value.Name })
            {
                var read = Wrap(nameof(ReadThis));
                var first = Construct(name, read)!; var second = Construct(name, read)!;
                Assert.Equal(runtime.ObjectStorage.Type.Name, first.GetType().Name); Assert.NotSame(first, second);
                var prototype = type.GetMethod(runtime.FunctionIntrospection.GetProperty.Name)!.Invoke(null, [read, "prototype"]);
                Assert.Same(prototype, loaded.GetType(runtime.DescriptorStorage.SetPrototype.DeclaringType!.Name)!
                    .GetMethod(runtime.DescriptorStorage.GetPrototype.Name)!.Invoke(null, [first]));
                var result = new object(); Assert.Same(result, Construct(name, Wrap(nameof(ReturnObject)), result));
                Assert.Equal(runtime.ObjectStorage.Type.Name, Construct(name, Wrap(nameof(ReturnPrimitive)))!.GetType().Name);
                Assert.Same(previous, current.GetValue(null));
                var error = Assert.Throws<TargetInvocationException>(() => Construct(name, Wrap(nameof(Fail))));
                Assert.Same(Failure, error.InnerException); Assert.Same(previous, current.GetValue(null));
                foreach (var target in new object?[] { null, new Dictionary<string, object?>(), 3d })
                {
                    var invalid = Assert.Throws<TargetInvocationException>(() => Construct(name, target));
                    Assert.Contains("not a constructor", invalid.InnerException!.Message);
                    Assert.Same(previous, current.GetValue(null));
                }
            }
            Assert.Equal(7d, Assert.IsType<HostConstructor>(Construct(owner.Value.Name, typeof(HostConstructor), 7d)).Value);
            var boxed = Construct(owner.Value.Name, typeof(string), "abc");
            Assert.Equal(3d, type.GetMethod(runtime.ObjectRead.Property.Name)!.Invoke(null, [boxed, "length"]));
            if (runtime.RegExps.Implementation is not null)
            {
                var regexp = Construct(owner.Value.Name, loaded.GetType(runtime.RegExps.RequireImplementation().Type.Name)!, "a", "g");
                Assert.Equal("a", type.GetMethod(runtime.ObjectRead.Property.Name)!.Invoke(null, [regexp, "source"]));
                Assert.Equal("g", type.GetMethod(runtime.ObjectRead.Property.Name)!.Invoke(null, [regexp, "flags"]));
            }
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Fact]
    public void OwnerAndHelpersUseExplicitDependencies()
    {
        Assert.Equal(2, Properties.Length);
        Assert.NotSame(new EmittedRuntime().DynamicConstruction, new EmittedRuntime().DynamicConstruction);
        Assert.Null(typeof(EmittedRuntime).GetProperty("DynamicConstruction")!.SetMethod);
        foreach (string old in new[] { "NewOnFunction", "ConstructDynamicValue" }) Assert.Null(typeof(EmittedRuntime).GetProperty(old));
        foreach (string name in new[] { "EmitNewOnFunction", "EmitConstructDynamicValue" })
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, Members)!;
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
            foreach (var parameter in method.GetParameters().Where(p => p.ParameterType.IsNested))
                Assert.DoesNotContain(parameter.ParameterType.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
        }
    }

    private static readonly Exception Failure = new InvalidOperationException("same constructor exception");
    public static object ReadThis(object __this) => __this;
    public static object ReturnObject(object __this, object value) => value;
    public static object ReturnPrimitive(object __this) => 7d;
    public static object Fail(object __this) => throw Failure;
    public sealed class HostConstructor(double value) { public double Value { get; } = value; }
    private static Dictionary<string, MethodBuilder> NewHandles()
    {
        var type = NewAssembly().DefineDynamicModule("main").DefineType("Handles");
        return new[] { "Function", "Value" }.ToDictionary(n => n, n => type.DefineMethod(n, MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes));
    }
    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static RuntimeFeatureSet Detect(string code) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(code).ScanTokens()).ParseOrThrow());
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"dynamic_construction_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes)); return Assembly.Load(bytes.ToArray());
    }
}
