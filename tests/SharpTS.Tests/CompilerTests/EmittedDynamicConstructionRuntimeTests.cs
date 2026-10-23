using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedDynamicConstructionRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static PropertyInfo[] Properties => typeof(EmittedDynamicConstructionRuntime).GetProperties()
        .Where(p => p.PropertyType != typeof(bool)).ToArray();
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
        foreach (var item in Properties)
        {
            Expect<InvalidOperationException>(() => item.SetValue(owner, handles[item.Name]));
            Expect<InvalidOperationException>(() => item.SetValue(owner, null));
            Assert.Same(handles[item.Name], item.GetValue(owner));
        }
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
    public async Task ReusedEmitterPreservesConstructionAndRestoresAmbientReceiver(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted); var owners = new HashSet<object>();
        foreach (string code in new[] { "const n=1;", "const r=new RegExp('a','g');function F(){}const c:any=F;new c();", "const P:any=Promise;", "const n=2;" })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(code));
            var owner = runtime.DynamicConstruction; Assert.True(owners.Add(owner)); Assert.True(owner.IsComplete);
            foreach (var property in Properties) Assert.Same(builder, ((MemberInfo)property.GetValue(owner)!).Module.Assembly);
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
            Assert.Equal(3d, Assert.IsType<HostConstructor>(Construct(owner.Value.Name, typeof(HostConstructor), "abc")).Value);
            object[] values = [1d, 2d];
            Assert.Same(values, Assert.IsType<HostArrayConstructor>(
                Construct(owner.Value.Name, typeof(HostArrayConstructor), (object)values)).Values);
            var boxed = Construct(owner.Value.Name, typeof(string), "abc");
            Assert.Equal(3d, type.GetMethod(runtime.ObjectRead.Property.Name)!.Invoke(null, [boxed, "length"]));
            var boxedNumber = Construct(owner.Value.Name, typeof(double), "7", "ignored");
            Assert.True((bool)type.GetMethod(runtime.BoxedPrimitives.IsOfType.Name)!
                .Invoke(null, [boxedNumber, "Number"])!);
            Assert.Equal(7d, type.GetMethod(runtime.BoxedPrimitives.UnwrapIfBoxed.Name)!
                .Invoke(null, [boxedNumber]));
            Assert.NotSame(boxedNumber, Construct(owner.Value.Name, typeof(double), "7"));
            Assert.Equal(0d, type.GetMethod(runtime.BoxedPrimitives.UnwrapIfBoxed.Name)!
                .Invoke(null, [Construct(owner.Value.Name, typeof(double))]));
            if (runtime.RegExps.Implementation is not null)
            {
                var regexp = Construct(owner.Value.Name, loaded.GetType(runtime.RegExps.RequireImplementation().Type.Name)!, "a", "g");
                Assert.Equal("a", type.GetMethod(runtime.ObjectRead.Property.Name)!.Invoke(null, [regexp, "source"]));
                Assert.Equal("g", type.GetMethod(runtime.ObjectRead.Property.Name)!.Invoke(null, [regexp, "flags"]));
            }
            if (runtime.Promise is not null)
            {
                var promise = Assert.IsAssignableFrom<Task<object>>(
                    Construct(owner.Value.Name, typeof(Task<object>), Wrap(nameof(ResolvePromise))));
                Assert.True(promise.IsCompleted);
                Assert.Equal(11d, await promise);
            }
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Fact]
    public void OwnerAndHelpersUseExplicitDependencies()
    {
        (string Name, Type Type)[] ownerApi =
        [
            (nameof(EmittedDynamicConstructionRuntime.ConstructWithNewTarget), typeof(MethodBuilder)),
            (nameof(EmittedDynamicConstructionRuntime.FactoryAttributeConstructor), typeof(ConstructorBuilder)),
            (nameof(EmittedDynamicConstructionRuntime.FactoryAttributeName), typeof(FieldBuilder)),
            (nameof(EmittedDynamicConstructionRuntime.FactoryAttributeType), typeof(TypeBuilder)),
            (nameof(EmittedDynamicConstructionRuntime.Function), typeof(MethodBuilder)),
            (nameof(EmittedDynamicConstructionRuntime.FunctionWithNewTarget), typeof(MethodBuilder)),
            (nameof(EmittedDynamicConstructionRuntime.IsComplete), typeof(bool)),
            (nameof(EmittedDynamicConstructionRuntime.RecordClassReturn), typeof(MethodBuilder)),
            (nameof(EmittedDynamicConstructionRuntime.ResolveClassReturn), typeof(MethodBuilder)),
            (nameof(EmittedDynamicConstructionRuntime.Value), typeof(MethodBuilder))
        ];
        Assert.Equal(ownerApi, typeof(EmittedDynamicConstructionRuntime)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(property => (Name: property.Name, Type: property.PropertyType))
            .OrderBy(property => property.Name, StringComparer.Ordinal));
        Assert.Equal(ownerApi.Where(property => property.Type != typeof(bool)).Select(property => property.Name),
            NewHandles().Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.NotSame(new EmittedRuntime().DynamicConstruction, new EmittedRuntime().DynamicConstruction);
        Assert.Null(typeof(EmittedRuntime).GetProperty("DynamicConstruction")!.SetMethod);
        foreach (string old in new[] { "NewOnFunction", "ConstructDynamicValue" }) Assert.Null(typeof(EmittedRuntime).GetProperty(old));
        var contextParameters = new Dictionary<string, (string Name, Type Type)[]>
        {
            ["EmitNewOnFunction"] =
            [
                ("ClassDefinitions", typeof(EmittedClassDefinitionRuntime)),
                ("DescriptorStorage", typeof(EmittedDescriptorStorageRuntime)),
                ("Errors", typeof(EmittedErrorRuntime)),
                ("FunctionBindings", typeof(EmittedFunctionBindingRuntime)),
                ("FunctionIntrospection", typeof(EmittedFunctionIntrospectionRuntime)),
                ("FunctionValues", typeof(EmittedFunctionValueRuntime)),
                ("ObjectRead", typeof(EmittedObjectReadRuntime)),
                ("ObjectStorage", typeof(EmittedObjectStorageRuntime)),
                ("ReflectedMethods", typeof(EmittedReflectedMethodRuntime)),
                ("SymbolType", typeof(Type)),
                ("TypeOf", typeof(MethodInfo)),
                ("UndefinedInstance", typeof(FieldInfo))
            ],
            ["EmitConstructDynamicValue"] =
            [
                ("ArrayConstructor", typeof(MethodInfo)),
                ("ArrayStorage", typeof(EmittedArrayStorageRuntime)),
                ("Booleans", typeof(EmittedBooleanRuntime)),
                ("BoxedPrimitives", typeof(EmittedBoxedPrimitiveRuntime)),
                ("ClassDefinitions", typeof(EmittedClassDefinitionRuntime)),
                ("DescriptorStorage", typeof(EmittedDescriptorStorageRuntime)),
                ("DynamicConstruction", typeof(EmittedDynamicConstructionRuntime)),
                ("Errors", typeof(EmittedErrorRuntime)),
                ("FunctionConstruction", typeof(EmittedFunctionConstructionRuntime)),
                ("FunctionValues", typeof(EmittedFunctionValueRuntime)),
                ("NumericCoercion", typeof(EmittedNumericCoercionRuntime)),
                ("ObjectRead", typeof(EmittedObjectReadRuntime)),
                ("Operators", typeof(EmittedOperatorRuntime)),
                ("Promise", typeof(EmittedPromiseRuntime)),
                ("RegExps", typeof(EmittedRegExpRuntime)),
                ("StringCoercion", typeof(EmittedStringCoercionRuntime)),
                ("UndefinedInstance", typeof(FieldInfo)),
                ("UndefinedType", typeof(Type))
            ],
            ["EmitClassConstructorReturns"] =
            [
                ("Errors", typeof(EmittedErrorRuntime)),
                ("SymbolType", typeof(Type)),
                ("UndefinedType", typeof(Type))
            ]
        };
        foreach (string name in contextParameters.Keys)
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, Members)!;
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
            var contexts = method.GetParameters().Where(p => p.ParameterType.IsNested).ToArray();
            Assert.NotEmpty(contexts);
            foreach (var parameter in contexts)
            {
                Assert.DoesNotContain(parameter.ParameterType.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
                var constructors = parameter.ParameterType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.NotEmpty(constructors);
                foreach (var constructor in constructors)
                    Assert.Equal(contextParameters[name], constructor.GetParameters()
                        .Select(input => (Name: input.Name!, Type: input.ParameterType))
                        .OrderBy(input => input.Name, StringComparer.Ordinal));
            }
        }
    }

    private static readonly Exception Failure = new InvalidOperationException("same constructor exception");
    public static object ReadThis(object __this) => __this;
    public static object ReturnObject(object __this, object value) => value;
    public static object ReturnPrimitive(object __this) => 7d;
    public static object Fail(object __this) => throw Failure;
    public static object? ResolvePromise(object resolve, object reject)
    {
        resolve.GetType().GetMethod("Invoke")!.Invoke(resolve, [new object[] { 11d }]);
        return null;
    }
    public sealed class HostConstructor
    {
        public HostConstructor(double value) { Value = value; }
        public HostConstructor(string value) { Value = value.Length; }
        public double Value { get; }
    }
    public sealed class HostArrayConstructor(object[] values) { public object[] Values { get; } = values; }
    private static Dictionary<string, MemberInfo> NewHandles()
    {
        var type = NewAssembly().DefineDynamicModule("main").DefineType("Handles");
        return new Dictionary<string, MemberInfo>
        {
            ["Function"] = type.DefineMethod("Function", MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes),
            ["Value"] = type.DefineMethod("Value", MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes),
            ["ConstructWithNewTarget"] = type.DefineMethod("ConstructWithNewTarget", MethodAttributes.Public | MethodAttributes.Static,
                typeof(object), [typeof(object), typeof(object[]), typeof(object)]),
            ["FunctionWithNewTarget"] = type.DefineMethod("FunctionWithNewTarget", MethodAttributes.Public | MethodAttributes.Static,
                typeof(object), [typeof(object), typeof(object[]), typeof(object)]),
            ["RecordClassReturn"] = type.DefineMethod("RecordClassReturn", MethodAttributes.Public | MethodAttributes.Static,
                typeof(void), [typeof(object), typeof(object), typeof(bool), typeof(object)]),
            ["ResolveClassReturn"] = type.DefineMethod("ResolveClassReturn", MethodAttributes.Public | MethodAttributes.Static,
                typeof(object), [typeof(object)]),
            ["FactoryAttributeType"] = type,
            ["FactoryAttributeConstructor"] = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(string)]),
            ["FactoryAttributeName"] = type.DefineField("Name", typeof(string), FieldAttributes.Public)
        };
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
