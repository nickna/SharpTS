using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;
using static SharpTS.Tests.CompilerTests.NativeSourceInvocationPlanTests;

namespace SharpTS.Tests.CompilerTests;

public sealed class NativeClassInitializationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefinitionPositionDeferredKeysAndClosedConstraintOwnerArePreserved(bool native)
    {
        const string source = """
            const events: string[] = [];
            function note(value: string): string { events.push(value); return value; }
            note('before');
            class Bound {}
            class Generic<T extends Bound> {
                static first = note('static');
                static [note('key')] = note('value');
            }
            note('after');
            function observe(a: any, b: any, c: any) { return b; }
            const value = observe(note('arg-before'), class { static item = note('expression'); }, note('arg-after'));
            console.log(events.join(','));
            """;
        Assembly saved = Compile(source, native);
        Assert.Equal("before,key,static,value,after,arg-before,expression,arg-after\n", Run(saved));
        var stubs = Stubs(saved);
        if (!native) { Assert.Empty(stubs); return; }
        Assert.NotEmpty(stubs);
        foreach (var stub in stubs)
        {
            Assert.True(stub.IsAssembly);
            Assert.False(stub.IsGenericMethod);
            Assert.DoesNotContain(Instructions(stub), instruction => instruction.Member?.Name == "GetTypeFromHandle");
            Assert.Contains(Instructions(stub), instruction => instruction.Member?.Name == "RunClassConstructor");
        }
        Assert.Contains(stubs.SelectMany(Instructions), instruction => instruction.Code == OpCodes.Ldtoken &&
            instruction.Member is Type type && type.IsGenericType && type.GetGenericTypeDefinition().Name == "Generic" &&
            type.GetGenericArguments().Single().Name == "Bound");
        Assert.DoesNotContain(saved.GetTypes().SelectMany(Methods).SelectMany(Instructions),
            instruction => instruction.Member is MethodInfo { Name: "RunClassDefinition" });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuspendedDeclarationsKeepTimingAndGuestThrowIdentity(bool native)
    {
        const string source = """
            const events: string[] = [];
            const marker = { id: 'same' };
            function record(value: string) { events.push(value); return 1; }
            function fail(): number { throw marker; }
            async function work() {
                events.push('async-before');
                await Promise.resolve(0);
                class Later { static value = record('async-static'); }
                events.push('async-after');
                try { class Failed { static value = fail(); } }
                catch (error) { events.push(error === marker ? 'async-caught' : 'wrong'); }
            }
            function* sequence() {
                events.push('gen-before');
                yield 0;
                class Later { static value = record('gen-static'); }
                events.push('gen-after');
                try { class Failed { static value = fail(); } }
                catch (error) { events.push(error === marker ? 'gen-caught' : 'wrong'); }
            }
            async function run() {
                await work();
                const iterator = sequence(); iterator.next(); iterator.next();
                console.log(events.join(','));
            }
            run();
            """;
        Assembly saved = Compile(source, native);
        Assert.Equal("async-before,async-static,async-after,async-caught,gen-before,gen-static,gen-after,gen-caught\n", Run(saved));
        if (native) Assert.NotEmpty(Stubs(saved)); else Assert.Empty(Stubs(saved));
    }

    [Fact]
    public void LiteralInitializerUnwrapsTheOriginalCachedFailureWithoutRunningEarly()
    {
        var assembly = NewAssembly(); var module = assembly.DefineDynamicModule("main");
        var program = module.DefineType("Program", TypeAttributes.Public);
        var state = module.DefineType("State", TypeAttributes.Public);
        var failure = state.DefineField("Failure", typeof(Exception), FieldAttributes.Public | FieldAttributes.Static);
        var count = state.DefineField("Count", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        var owner = module.DefineType("Failure", TypeAttributes.Public);
        var cctor = owner.DefineTypeInitializer().GetILGenerator();
        cctor.Emit(OpCodes.Ldsfld, count); cctor.Emit(OpCodes.Ldc_I4_1); cctor.Emit(OpCodes.Add); cctor.Emit(OpCodes.Stsfld, count);
        cctor.Emit(OpCodes.Ldsfld, failure); cctor.Emit(OpCodes.Throw);
        var emission = new NativeClassInitializationEmission(program, TypeProvider.Runtime, new());
        var caller = program.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        var il = caller.GetILGenerator(); Assert.True(emission.TryEmit(il, owner)); il.Emit(OpCodes.Ret);
        emission.CompleteEmission();
        Assert.Throws<InvalidOperationException>(() => emission.TryEmit(il, owner));
        owner.CreateType(); state.CreateType(); program.CreateType();
        Assembly saved = Load(assembly);
        Type savedState = saved.GetType("State")!;
        Assert.Equal(0, savedState.GetField("Count")!.GetValue(null));
        var original = new InvalidOperationException("same");
        savedState.GetField("Failure")!.SetValue(null, original);
        for (int i = 0; i < 2; i++)
            Assert.Same(original, Assert.Throws<TargetInvocationException>(() => saved.GetType("Program")!.GetMethod("Run")!.Invoke(null, null)).InnerException);
        Assert.Equal(1, savedState.GetField("Count")!.GetValue(null));
    }

    [Fact]
    public void OpenCanonicalOwnerKeepsTheLegacyPathWithoutGuessingObject()
    {
        var assembly = NewAssembly(); var module = assembly.DefineDynamicModule("main");
        var program = module.DefineType("Program", TypeAttributes.Public);
        var owner = module.DefineType("Open", TypeAttributes.Public);
        var parameter = owner.DefineGenericParameters("T")[0];
        var parameters = new ClassGenericParameterRegistry(); parameters.Declare(owner, [parameter], [parameter]);
        var emission = new NativeClassInitializationEmission(program, TypeProvider.Runtime, parameters);
        var caller = program.DefineMethod("Run", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        var il = caller.GetILGenerator();
        Assert.False(emission.TryEmit(il, owner));
        Assert.Equal(0, il.ILOffset);
        Assert.Equal(1, emission.LegacyFallbackCount);
        emission.CompleteEmission();
    }

    private static IEnumerable<MethodBase> Methods(Type type) => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
        .Where(method => method.DeclaringType == type && method.GetMethodBody() is not null);
    private static MethodInfo[] Stubs(Assembly assembly) => assembly.GetType("$Program")!.GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
        .Where(method => method.Name.StartsWith("<>NativeInitializeClass", StringComparison.Ordinal)).ToArray();
    private static string Run(Assembly assembly) => TestHarness.RunCompiledWithTimeout(() => assembly, TimeSpan.FromSeconds(20));
    private static Assembly Compile(string source, bool native)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var types = new TypeChecker().Check(statements);
        var compiler = new ILCompiler("native_init_" + Guid.NewGuid().ToString("N"), false, true, null, null, null, OutputTarget.Dll);
        if (native) compiler.EnableNativeAotProjectOutput();
        compiler.Compile(statements, types);
        return Verify(compiler.SaveToBytes());
    }
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName("native_init_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
    private static Assembly Load(PersistedAssemblyBuilder assembly) { using var stream = new MemoryStream(); assembly.Save(stream); return Verify(stream.ToArray()); }
    private static Assembly Verify(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(stream)); return Assembly.Load(bytes);
    }
}
