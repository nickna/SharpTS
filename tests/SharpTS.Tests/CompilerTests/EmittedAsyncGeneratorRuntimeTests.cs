using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedAsyncGeneratorRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static PropertyInfo[] Slots(Type type) => type.GetProperties().Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType)).ToArray();
    public static IEnumerable<object[]> Declarations => new[] { typeof(EmittedAsyncGeneratorRuntime), typeof(EmittedAsyncGeneratorContinuationRuntime), typeof(EmittedAsyncFromSyncRuntime) }
        .SelectMany(type => Slots(type).Select(p => new object[] { type, p.Name }));

    [Theory]
    [MemberData(nameof(Declarations))]
    public void MissingDeclarationCanBeRepairedAndCompletionFreezesMetadata(Type ownerType, string missing)
    {
        object owner = Activator.CreateInstance(ownerType, nonPublic: true)!;
        var handles = NewHandles(); var property = Slots(ownerType).Single(p => p.Name == missing);
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        foreach (var other in Slots(ownerType).Where(p => p.Name != missing)) other.SetValue(owner, handles[other.PropertyType]);
        var complete = ownerType.GetMethod("CompleteEmission", Members)!;
        Expect<InvalidOperationException>(() => complete.Invoke(owner, null));
        Assert.Equal(false, ownerType.GetProperty("IsComplete")!.GetValue(owner));
        property.SetValue(owner, handles[property.PropertyType]);
        Assert.Same(handles[property.PropertyType], property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, handles[property.PropertyType]));
        complete.Invoke(owner, null); Assert.Equal(true, ownerType.GetProperty("IsComplete")!.GetValue(owner));
        Expect<InvalidOperationException>(() => complete.Invoke(owner, null));
        foreach (var item in Slots(ownerType)) Expect<InvalidOperationException>(() => item.SetValue(owner, handles[item.PropertyType]));
    }

    [Fact]
    public void RootAndChildrenHaveIndependentCheckedAvailability()
    {
        var first = new EmittedRuntime(); var second = new EmittedRuntime();
        Assert.Null(first.AsyncGenerators); Assert.Throws<InvalidOperationException>(() => first.RequireAsyncGenerators());
        first.BeginAsyncGeneratorsEmission(); second.BeginAsyncGeneratorsEmission();
        var root = first.RequireAsyncGenerators(); Assert.Same(first.AsyncGenerators, root); Assert.NotSame(root, second.AsyncGenerators);
        Assert.Throws<InvalidOperationException>(first.BeginAsyncGeneratorsEmission);
        Assert.Null(root.Continuations); Assert.Null(root.FromSync);
        Assert.Throws<InvalidOperationException>(() => root.RequireContinuations());
        Assert.Throws<InvalidOperationException>(() => root.RequireFromSync());
        root.BeginContinuationsEmission(); second.RequireAsyncGenerators().BeginContinuationsEmission();
        Assert.NotSame(root.Continuations, second.RequireAsyncGenerators().Continuations);
        Assert.Null(root.FromSync); Assert.Throws<InvalidOperationException>(root.BeginContinuationsEmission);
        root.BeginFromSyncEmission(); Assert.Same(root.FromSync, root.RequireFromSync());
        Assert.Throws<InvalidOperationException>(root.BeginFromSyncEmission);
    }

    [Fact]
    public void LaterChildCanCompleteAfterAnEarlierChildWithoutFreezingAnIncompleteRoot()
    {
        var runtime = new EmittedRuntime(); runtime.BeginAsyncGeneratorsEmission(); var root = runtime.RequireAsyncGenerators();
        SetAll(root); root.BeginContinuationsEmission(); SetAll(root.RequireContinuations());
        Assert.Throws<InvalidOperationException>(root.CompleteEmission);
        root.RequireContinuations().CompleteEmission();
        root.BeginFromSyncEmission();
        Assert.Throws<InvalidOperationException>(root.CompleteEmission);
        Assert.False(root.IsComplete); Assert.True(root.RequireContinuations().IsComplete);
        Assert.Throws<InvalidOperationException>(root.RequireFromSync().CompleteEmission);
        SetAll(root.RequireFromSync()); root.RequireFromSync().CompleteEmission(); root.CompleteEmission();
        Assert.True(root.IsComplete); Assert.True(root.RequireFromSync().IsComplete);
        Assert.Throws<InvalidOperationException>(root.BeginFromSyncEmission);
        Assert.Throws<InvalidOperationException>(root.BeginContinuationsEmission);
        Assert.Throws<InvalidOperationException>(root.CompleteEmission);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterPreservesIndependentAsyncAndForAwaitGates(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted); var owners = new HashSet<object>();
        var cases = new[]
        {
            (Source: "const n=1;", Async: false, ForAwait: false),
            (Source: "async function* values(){yield 1;}", Async: true, ForAwait: false),
            (Source: "async function run(){for await(const value of [1,2]){}}run();", Async: false, ForAwait: true),
            (Source: "async function* values(){yield 1;}async function run(){for await(const value of values()){}}run();", Async: true, ForAwait: true),
            (Source: "const n=2;", Async: false, ForAwait: false)
        };
        foreach (var item in cases)
        {
            var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer(item.Source).ScanTokens()).ParseOrThrow());
            Assert.Equal(item.Async, features.UsesAsyncGenerator); Assert.Equal(item.ForAwait, features.UsesForAwaitOf);
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), features);
            Assert.Equal(item.Async || item.ForAwait, runtime.AsyncGenerators is not null);
            if (runtime.AsyncGenerators is { } root)
            {
                CheckOwner(root, builder, owners);
                Assert.Equal(item.Async, root.Continuations is not null); Assert.Equal(item.ForAwait, root.FromSync is not null);
                if (root.Continuations is { } continuations) CheckOwner(continuations, builder, owners);
                else Assert.Throws<InvalidOperationException>(() => root.RequireContinuations());
                if (root.FromSync is { } fromSync) CheckOwner(fromSync, builder, owners);
                else Assert.Throws<InvalidOperationException>(() => root.RequireFromSync());
            }
            else Assert.Throws<InvalidOperationException>(() => runtime.RequireAsyncGenerators());
            var loaded = SaveVerifyLoad(builder); var generator = loaded.GetType("$IAsyncGenerator");
            Assert.Equal(item.Async || item.ForAwait, generator is not null);
            if (generator is not null)
            {
                Assert.True(generator.IsPublic && generator.IsInterface);
                Assert.Contains(typeof(IAsyncEnumerator<object>), generator.GetInterfaces());
                Assert.Contains(typeof(IAsyncEnumerable<object>), generator.GetInterfaces());
                foreach (string name in new[] { "next", "return", "throw" })
                {
                    var method = generator.GetMethod(name)!;
                    Assert.Equal(typeof(Task<object>), method.ReturnType);
                    Assert.Equal(typeof(object), Assert.Single(method.GetParameters()).ParameterType);
                    Assert.True(method.IsPublic && method.IsAbstract && method.IsVirtual);
                }
            }
            var runtimeType = loaded.GetType("$Runtime")!;
            foreach (string name in new[] { "AsyncGeneratorAwaitContinue", "AsyncGeneratorBuildResult" })
                Assert.Equal(item.Async, runtimeType.GetMethod(name, Members) is not null);
            Assert.Equal(item.ForAwait, loaded.GetType("$AsyncFromSyncIterator") is not null);
            foreach (string name in new[] { "AsyncFromSyncCreateResult", "AsyncFromSyncAwaitResult", "AsyncFromSyncAwaitContinuation", "AdaptSyncIterableToAsyncGenerator" })
                Assert.Equal(item.ForAwait, runtimeType.GetMethod(name, Members) is not null);
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Fact]
    public void HelpersUseExplicitDependenciesAndAdapterHandlesLeaveTheEmitter()
    {
        Assert.Equal(4, Slots(typeof(EmittedAsyncGeneratorRuntime)).Length);
        Assert.Equal(2, Slots(typeof(EmittedAsyncGeneratorContinuationRuntime)).Length);
        Assert.Equal(5, Slots(typeof(EmittedAsyncFromSyncRuntime)).Length);
        foreach (string name in new[] { "_asyncFromSyncCreateResult", "_asyncFromSyncAwaitResult", "_asyncFromSyncAwaitContinuation" })
            Assert.Null(typeof(RuntimeEmitter).GetField(name, Members));
        foreach (string name in new[] { "EmitAsyncGeneratorInterface", "EmitAsyncGeneratorAwaitContinueMethods", "EmitAsyncGeneratorBuildResultMethod", "EmitAsyncFromSyncIteratorSupport", "EmitAsyncFromSyncCreateResult", "EmitAsyncFromSyncAwaitContinuation", "EmitAsyncFromSyncAwaitResult", "EmitAsyncFromSyncIteratorType", "EmitAsyncFromSyncNext", "EmitAsyncFromSyncReturn", "EmitAsyncFromSyncThrow", "EmitAsyncFromSyncInheritedMembers", "EmitAdaptSyncIterableToAsyncGenerator" })
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, Members)!;
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
            foreach (var parameter in method.GetParameters().Where(p => p.ParameterType.IsNested))
                Assert.DoesNotContain(parameter.ParameterType.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
        }
        Assert.Contains(typeof(RuntimeEmitter).GetMethod("EmitAsyncFromSyncIteratorSupport", Members)!.GetParameters(), p => p.Name == "runtimeType" && p.ParameterType == typeof(TypeBuilder));
    }

    private static void CheckOwner(object owner, Assembly builder, HashSet<object> owners)
    {
        Assert.True(owners.Add(owner)); Assert.Equal(true, owner.GetType().GetProperty("IsComplete")!.GetValue(owner));
        foreach (var slot in Slots(owner.GetType())) Assert.Same(builder, ((MemberInfo)slot.GetValue(owner)!).Module.Assembly);
    }
    private static Dictionary<Type, object> NewHandles()
    {
        var type = NewAssembly().DefineDynamicModule("main").DefineType("Handles");
        return new() { [typeof(TypeBuilder)] = type, [typeof(MethodBuilder)] = type.DefineMethod("Method", MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes), [typeof(ConstructorBuilder)] = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes) };
    }
    private static void SetAll(object owner)
    {
        var handles = NewHandles(); foreach (var slot in Slots(owner.GetType())) slot.SetValue(owner, handles[slot.PropertyType]);
    }
    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"async_generator_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes)); return Assembly.Load(bytes.ToArray());
    }
}
