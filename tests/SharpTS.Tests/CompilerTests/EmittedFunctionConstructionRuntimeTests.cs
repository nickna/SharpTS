using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedFunctionConstructionRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static PropertyInfo[] Metadata => typeof(EmittedFunctionConstructionRuntime).GetProperties()
        .Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType)).ToArray();
    public static IEnumerable<object[]> Handles => Metadata.Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(Handles))]
    public void HandlesRequireDeclarationAndFreezeAfterCompletion(string name)
    {
        var owner = new EmittedFunctionConstructionRuntime(); var property = typeof(EmittedFunctionConstructionRuntime).GetProperty(name)!;
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        var probe = NewAssembly().DefineDynamicModule("main").DefineType("Handles");
        object Handle(PropertyInfo p) => p.PropertyType == typeof(ConstructorBuilder)
            ? probe.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes)
            : probe.DefineMethod(p.Name, MethodAttributes.Public, typeof(void), Type.EmptyTypes);
        foreach (var p in Metadata.Where(p => p.Name != name)) p.SetValue(owner, Handle(p));
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        var value = Handle(property); property.SetValue(owner, value); Assert.Same(value, property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, value));
        owner.CompleteEmission(); Assert.True(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Expect<InvalidOperationException>(() => property.SetValue(owner, value));
        Assert.Same(value, property.GetValue(owner));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedEmissionHasFreshConstructionAndCaches(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<object>(); var caches = new HashSet<object>();
        foreach (string code in new[] { "const n=1;", "function f(...xs:number[]){return xs.length;}f(1,2);", "const n=2;" })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(code));
            var owner = runtime.FunctionConstruction; Assert.True(owners.Add(owner)); Assert.True(owner.IsComplete);
            foreach (var p in Metadata)
            {
                var member = Assert.IsAssignableFrom<MemberInfo>(p.GetValue(owner));
                Assert.Same(builder, member.Module.Assembly);
                Assert.True(Assert.IsAssignableFrom<TypeBuilder>(member.DeclaringType).IsCreated());
            }
            var loaded = SaveVerifyLoad(builder); var function = loaded.GetType("$TSFunction")!;
            foreach (string name in new[] { "_instanceCache", "_prototypeCache", "_invokerCache", "_thisFieldCache" })
            {
                var cache = function.GetField(name, Members)!.GetValue(null)!;
                Assert.True(caches.Add(cache));
                Assert.All(((IEnumerable)cache).Cast<object>(), entry => Assert.Same(loaded, entry.GetType().GetProperty("Key")!.GetValue(entry) switch
                {
                    MethodInfo method => method.Module.Assembly,
                    Type type => type.Assembly,
                    object tuple => ((MethodInfo)tuple.GetType().GetField("Item1")!.GetValue(tuple)!).Module.Assembly,
                    _ => throw new InvalidOperationException("Missing cache key")
                }));
            }
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Fact]
    public void FactoryKeysPreserveFunctionIdentityWhileConstructorsShareInvokers()
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var runtime = new RuntimeEmitter(TypeProvider.Runtime).EmitAll(module, Detect("const n=1;"));
        var probe = module.DefineType("Probe", TypeAttributes.Public);
        var read = probe.DefineMethod("Read", MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
        read.GetILGenerator().Emit(OpCodes.Ldnull); read.GetILGenerator().Emit(OpCodes.Ret); probe.CreateType();
        var assembly = SaveVerifyLoad(builder); var function = assembly.GetType("$TSFunction")!;
        var method = assembly.GetType("Probe")!.GetMethod("Read")!;
        object Get(string name, int length) => function.GetMethod(runtime.FunctionConstruction.GetOrCreate.Name)!.Invoke(null, [method, name, length])!;
        var first = Get("first", 0); Assert.Same(first, Get("first", 0));
        var renamed = Get("alias", 0); var resized = Get("first", 2);
        Assert.NotSame(first, renamed); Assert.NotSame(first, resized);
        Assert.Equal("first", function.GetProperty("Name")!.GetValue(first));
        Assert.Equal("alias", function.GetProperty("Name")!.GetValue(renamed));
        Assert.Equal(2, function.GetProperty("Length")!.GetValue(resized));
        var invoker = function.GetField("_invoker", Members)!;
        Assert.Same(invoker.GetValue(first), invoker.GetValue(renamed));
        Assert.Same(invoker.GetValue(first), invoker.GetValue(resized));
        Assert.Same(method, function.GetMethod("GetMethodInfo")!.Invoke(first, null));
        var uncached = Activator.CreateInstance(function, new object?[] { null, method })!;
        var cached = Activator.CreateInstance(function, new object?[] { null, method, "cached", 3 })!;
        Assert.NotSame(first, uncached); Assert.NotSame(first, cached);
        Assert.Same(invoker.GetValue(first), invoker.GetValue(uncached));
        Assert.Same(invoker.GetValue(first), invoker.GetValue(cached));
        Assert.Equal("cached", function.GetProperty("Name")!.GetValue(cached));
        Assert.Equal(3, function.GetProperty("Length")!.GetValue(cached));
        var constructed = function.GetMethod(runtime.FunctionConstruction.Construct.Name)!.Invoke(null, [Array.Empty<object>()])!;
        Assert.Equal("anonymous", function.GetProperty("Name")!.GetValue(constructed));
        Assert.Equal(0, function.GetProperty("Length")!.GetValue(constructed));
    }

    [Fact]
    public void OwnerAndConstructorHelperHaveExplicitBoundaries()
    {
        Assert.Equal(4, Metadata.Length);
        Assert.NotSame(new EmittedRuntime().FunctionConstruction, new EmittedRuntime().FunctionConstruction);
        Assert.Null(typeof(EmittedRuntime).GetProperty(nameof(EmittedRuntime.FunctionConstruction))!.SetMethod);
        var method = typeof(RuntimeEmitter).GetMethod("EmitFunctionConstructor", Members)!;
        Assert.Contains(method.GetParameters(), p => p.ParameterType == typeof(EmittedFunctionConstructionRuntime));
        Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
        foreach (var p in method.GetParameters().Where(p => p.ParameterType.IsNestedPrivate))
            Assert.DoesNotContain(p.ParameterType.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
        foreach (string name in new[] { "TSFunctionCtor", "TSFunctionCtorWithCache", "TSFunctionGetOrCreate", "FunctionConstructor" })
            Assert.Null(typeof(EmittedRuntime).GetProperty(name));
    }

    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static RuntimeFeatureSet Detect(string code) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(code).ScanTokens()).ParseOrThrow());
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"function_construction_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes)); return Assembly.Load(bytes.ToArray());
    }
}
