using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedCallArgumentsRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static PropertyInfo[] Properties => typeof(EmittedCallArgumentsRuntime).GetProperties().Where(p => p.PropertyType == typeof(MethodBuilder)).ToArray();
    public static IEnumerable<object[]> Declarations => Properties.Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(Declarations))]
    public void MissingDeclarationCanBeRepairedAndCompletionFreezesMetadata(string missing)
    {
        var owner = new EmittedCallArgumentsRuntime(); var handles = NewHandles();
        var property = Properties.Single(p => p.Name == missing);
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        foreach (var other in Properties.Where(p => p.Name != missing)) other.SetValue(owner, handles[other.Name]);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        property.SetValue(owner, handles[missing]); Assert.Same(handles[missing], property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, handles[missing]));
        owner.CompleteEmission(); Assert.True(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        foreach (var item in Properties) Expect<InvalidOperationException>(() => item.SetValue(owner, handles[item.Name]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterPreservesThreadLocalPoolingAndSpreadExpansion(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted); var owners = new HashSet<object>();
        foreach (string code in new[] { "const n=1;", "function f(...xs:any[]){return xs;}const r=f(...[1,2]);", "const n=2;" })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(code));
            var owner = runtime.CallArguments; Assert.True(owners.Add(owner)); Assert.True(owner.IsComplete);
            foreach (var property in Properties) Assert.Same(builder, ((MethodInfo)property.GetValue(owner)!).Module.Assembly);
            var loaded = SaveVerifyLoad(builder);
            var pool = loaded.GetType(owner.PoolGet.DeclaringType!.Name)!;
            var get = pool.GetMethod(owner.PoolGet.Name)!;
            object[] Get(int arity) => (object[])get.Invoke(null, [arity])!;
            var fields = pool.GetFields(Members);
            Assert.Equal(4, fields.Length);
            Assert.All(fields, f => Assert.NotNull(f.GetCustomAttribute<ThreadStaticAttribute>()));
            var cached = new List<object[]>();
            for (int arity = 1; arity <= 4; arity++)
            {
                var first = Get(arity); Assert.Equal(arity, first.Length); Assert.All(first, Assert.Null);
                first[0] = arity; Assert.Same(first, Get(arity)); Assert.Equal(arity, Get(arity)[0]);
                cached.Add(first);
            }
            Assert.Equal(4, cached.Distinct(ReferenceEqualityComparer.Instance).Count());
            foreach (int arity in new[] { 0, 5, 8 })
            {
                var first = Get(arity); Assert.Equal(arity, first.Length); Assert.NotSame(first, Get(arity));
            }
            object[]? otherThread = null; Exception? workerFailure = null;
            var worker = new Thread(() =>
            {
                try { otherThread = Get(1); Assert.Null(otherThread[0]); Assert.Same(otherThread, Get(1)); }
                catch (Exception error) { workerFailure = error; }
            });
            worker.Start(); Assert.True(worker.Join(TimeSpan.FromSeconds(10))); Assert.Null(workerFailure);
            Assert.NotNull(otherThread); Assert.NotSame(cached[0], otherThread); Assert.Same(cached[0], Get(1));

            var runtimeType = loaded.GetType(owner.Expand.DeclaringType!.Name)!;
            var expand = runtimeType.GetMethod(owner.Expand.Name)!;
            var symbol = loaded.GetType(runtime.Symbols.Iterator.DeclaringType!.Name)!
                .GetField(runtime.Symbols.Iterator.Name, Members)!.GetValue(null);
            object[] Expand(object[] args, bool[] spread) => (object[])expand.Invoke(null, [args, spread, symbol, runtimeType])!;
            var marker = new object(); object[] plain = [marker, 4d];
            var copy = Expand(plain, [false, false]); Assert.NotSame(plain, copy); Assert.Equal(plain, copy);
            var list = new List<object> { 1d, 2d };
            var result = Expand([marker, list, "a\U0001f600", 9d], [false, true, true, false]);
            Assert.Equal(new object[] { marker, 1d, 2d, "a", "\U0001f600", 9d }, result);
            Assert.Equal(new object[] { 1d, 2d }, list); Assert.NotSame(result, Expand([list], [true]));
            Assert.Empty(Expand([], []));
            var error = Assert.Throws<TargetInvocationException>(() => Expand([null!], [true]));
            Assert.Contains("iterable", error.InnerException!.Message);
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Fact]
    public void OwnerAndHelpersUseExplicitDependencies()
    {
        Assert.Equal(2, Properties.Length);
        Assert.NotSame(new EmittedRuntime().CallArguments, new EmittedRuntime().CallArguments);
        Assert.Null(typeof(EmittedRuntime).GetProperty("CallArguments")!.SetMethod);
        foreach (string old in new[] { "CallArgsPoolGet", "ExpandCallArgs" }) Assert.Null(typeof(EmittedRuntime).GetProperty(old));
        foreach (string name in new[] { "EmitCallArgsPool", "EmitExpandCallArgs" })
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, Members)!;
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
            foreach (var parameter in method.GetParameters().Where(p => p.ParameterType.IsNested))
                Assert.DoesNotContain(parameter.ParameterType.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
        }
    }

    private static Dictionary<string, MethodBuilder> NewHandles()
    {
        var type = NewAssembly().DefineDynamicModule("main").DefineType("Handles");
        return new[] { "PoolGet", "Expand" }.ToDictionary(n => n, n => type.DefineMethod(n, MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes));
    }
    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static RuntimeFeatureSet Detect(string code) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(code).ScanTokens()).ParseOrThrow());
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"call_arguments_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes)); return Assembly.Load(bytes.ToArray());
    }
}
