using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedIteratorRecordRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static PropertyInfo[] Slots => typeof(EmittedIteratorRecordRuntime).GetProperties().Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType)).ToArray();

    [Theory]
    [InlineData("NextMethod")]
    [InlineData("InvokeNext")]
    [InlineData("InvokeNextWithSent")]
    public void MissingDeclarationCanBeRepairedAndCompletionFreezesMetadata(string missing)
    {
        var owner = new EmittedRuntime().IteratorRecords;
        var type = NewAssembly().DefineDynamicModule("main").DefineType("Handles");
        var handle = type.DefineMethod("Method", MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
        var property = Slots.Single(p => p.Name == missing);
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        foreach (var other in Slots.Where(p => p.Name != missing)) other.SetValue(owner, handle);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        property.SetValue(owner, handle); Assert.Same(handle, property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, handle));
        owner.CompleteEmission(); Assert.True(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        foreach (var slot in Slots) Expect<InvalidOperationException>(() => slot.SetValue(owner, handle));
    }

    [Fact]
    public void RequiredOwnerIsFreshForEachCompilation()
    {
        var first = new EmittedRuntime(); var second = new EmittedRuntime();
        Assert.NotSame(first.IteratorRecords, second.IteratorRecords);
        Assert.False(first.IteratorRecords.IsComplete); Assert.False(second.IteratorRecords.IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterPreservesValidationAndHelperAbi(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedIteratorRecordRuntime>();
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
            Assert.True(owners.Add(runtime.IteratorRecords)); Assert.True(runtime.IteratorRecords.IsComplete);
            foreach (var slot in Slots) Assert.Same(builder, ((MemberInfo)slot.GetValue(runtime.IteratorRecords)!).Module.Assembly);
            var loaded = SaveVerifyLoad(builder); var runtimeType = loaded.GetType("$Runtime")!;
            foreach (var (name, arity) in new[] { ("RequireIteratorObject", 1), ("GetIteratorNextMethod", 1), ("InvokeCapturedIteratorNext", 2), ("InvokeCapturedIteratorNextWithSent", 3) })
            {
                var method = runtimeType.GetMethod(name)!;
                Assert.True(method.IsPublic && method.IsStatic); Assert.Equal(typeof(object), method.ReturnType);
                Assert.Equal(Enumerable.Repeat(typeof(object), arity), method.GetParameters().Select(p => p.ParameterType));
            }
            var require = runtimeType.GetMethod("RequireIteratorObject")!; var valid = new Dictionary<string, object>();
            Assert.Same(valid, require.Invoke(null, [valid]));
            foreach (object? invalid in new object?[] { null, 1d, true, "value", new BigInteger(2) })
            {
                var error = Assert.Throws<TargetInvocationException>(() => require.Invoke(null, [invalid])).InnerException!;
                Assert.Contains("Iterator protocol requires an object", error.Message);
            }
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Fact]
    public void CapturedHelperUsesScopedInputsAndUnusedStoreIsRemoved()
    {
        Assert.Equal(3, Slots.Length); Assert.Null(typeof(EmittedRuntime).GetProperty("RequireIteratorObject"));
        var helper = typeof(RuntimeEmitter).GetMethod("EmitCapturedIteratorMethods", Members)!;
        Assert.Contains(helper.GetParameters(), p => p.ParameterType == typeof(EmittedIteratorRecordRuntime));
        Assert.DoesNotContain(helper.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
        var peers = helper.GetParameters().Single(p => p.Name == "inputs").ParameterType;
        Assert.Equal(new[] { "Errors", "Invocation", "ObjectRead", "Symbols", "UndefinedType" }, peers.GetProperties().Select(p => p.Name).Order());
        Assert.DoesNotContain(peers.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
    }

    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"iterator_record_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes)); return Assembly.Load(bytes.ToArray());
    }
}
