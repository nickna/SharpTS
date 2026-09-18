using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedIteratorWrapperRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static PropertyInfo[] Slots => typeof(EmittedIteratorWrapperRuntime).GetProperties().Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType)).ToArray();

    [Theory]
    [InlineData("Type")]
    [InlineData("Ctor")]
    [InlineData("MoveNextWithSent")]
    public void MissingDeclarationCanBeRepairedAndCompletionFreezesMetadata(string missing)
    {
        var owner = new EmittedRuntime().IteratorWrappers;
        var type = NewAssembly().DefineDynamicModule("main").DefineType("Handles");
        var handles = new Dictionary<string, object>
        {
            ["Type"] = type,
            ["Ctor"] = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes),
            ["MoveNextWithSent"] = type.DefineMethod("Method", MethodAttributes.Public, typeof(bool), [typeof(object)])
        };
        var property = Slots.Single(p => p.Name == missing);
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        foreach (var other in Slots.Where(p => p.Name != missing)) other.SetValue(owner, handles[other.Name]);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        property.SetValue(owner, handles[missing]); Assert.Same(handles[missing], property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, handles[missing]));
        owner.CompleteEmission(); Assert.True(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        foreach (var slot in Slots) Expect<InvalidOperationException>(() => slot.SetValue(owner, handles[slot.Name]));
    }

    [Fact]
    public void RequiredOwnerIsFreshForEachCompilation()
    {
        var first = new EmittedRuntime(); var second = new EmittedRuntime();
        Assert.NotSame(first.IteratorWrappers, second.IteratorWrappers);
        Assert.False(first.IteratorWrappers.IsComplete); Assert.False(second.IteratorWrappers.IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterPreservesWrapperAbiAndCapturedNext(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedIteratorWrapperRuntime>();
        foreach (string source in new[]
        {
            "const n=1;",
            "function* values(){yield 1;}for(const n of values()){}",
            "async function run(){for await(const n of [1,2]){}}run();",
            "const n=2;"
        })
        {
            var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), features);
            Assert.True(owners.Add(runtime.IteratorWrappers)); Assert.True(runtime.IteratorWrappers.IsComplete);
            foreach (var slot in Slots) Assert.Same(builder, ((MemberInfo)slot.GetValue(runtime.IteratorWrappers)!).Module.Assembly);
            var loaded = SaveVerifyLoad(builder); var wrapper = loaded.GetType("$IteratorWrapper")!;
            Assert.True(wrapper.IsPublic && wrapper.IsSealed);
            var ctor = Assert.Single(wrapper.GetConstructors());
            Assert.Equal(new[] { typeof(object), typeof(Type) }, ctor.GetParameters().Select(p => p.ParameterType));
            var sent = wrapper.GetMethod("MoveNextWithSent")!;
            Assert.Equal(typeof(bool), sent.ReturnType); Assert.Equal(typeof(object), Assert.Single(sent.GetParameters()).ParameterType);
            var calls = new List<object[]>();
            Func<object[], object> next = args =>
            {
                calls.Add(args.ToArray());
                return new Dictionary<string, object> { ["value"] = args.Length == 0 ? 10d : args[0], ["done"] = args.Length != 0 };
            };
            var iterator = new Dictionary<string, object> { ["next"] = next };
            var instance = (IEnumerator<object>)ctor.Invoke([iterator, typeof(string)]);
            Assert.Null(instance.Current); Assert.Null(((IEnumerator)instance).Current);
            iterator["next"] = new Func<object[], object>(_ => 99d);
            Assert.True(instance.MoveNext()); Assert.Equal(10d, instance.Current); Assert.Equal(instance.Current, ((IEnumerator)instance).Current);
            Assert.False((bool)sent.Invoke(instance, [9d])!); Assert.Equal(9d, instance.Current); Assert.Equal(instance.Current, ((IEnumerator)instance).Current);
            Assert.Equal(2, calls.Count); Assert.Empty(calls[0]); Assert.Equal(9d, Assert.Single(calls[1]));
            instance.Dispose(); Assert.Equal(2, calls.Count); Assert.Equal(9d, instance.Current);
            Assert.Equal("Reset is not supported for iterator wrappers", Assert.Throws<NotSupportedException>(instance.Reset).Message);
            var invalid = (IEnumerator<object>)ctor.Invoke([iterator, null]);
            Assert.Contains("Iterator protocol requires an object", Assert.ThrowsAny<Exception>(() => invalid.MoveNext()).Message);
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Fact]
    public void WrapperHelperReceivesOnlyOwnerAndExactDependencies()
    {
        Assert.Equal(3, Slots.Length);
        foreach (string old in new[] { "IteratorWrapperType", "IteratorWrapperCtor", "IteratorWrapperMoveNextWithSent" }) Assert.Null(typeof(EmittedRuntime).GetProperty(old));
        var helper = typeof(RuntimeEmitter).GetMethod("EmitIteratorWrapperType", Members)!;
        Assert.Contains(helper.GetParameters(), p => p.ParameterType == typeof(EmittedIteratorWrapperRuntime));
        Assert.DoesNotContain(helper.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
        var peers = helper.GetParameters().Single(p => p.Name == "inputs").ParameterType;
        Assert.Equal(new[] { "GetIteratorDone", "GetIteratorValue", "IteratorRecords" }, peers.GetProperties().Select(p => p.Name).Order());
        Assert.DoesNotContain(peers.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
    }

    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"iterator_wrapper_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes)); return Assembly.Load(bytes.ToArray());
    }
}
