using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedCancellationRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic;
    private static PropertyInfo[] Handles => typeof(EmittedCancellationRuntime).GetProperties()
        .Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType)).ToArray();

    [Theory]
    [InlineData("Requested")]
    [InlineData("Check")]
    [InlineData("BuildException")]
    public void MissingDeclarationCanBeRepairedAndCompletionFreezesEveryHandle(string missing)
    {
        var owner = new EmittedRuntime().Cancellation;
        var type = NewAssembly().DefineDynamicModule("main").DefineType("Handles");
        var declarations = new Dictionary<string, MemberInfo>
        {
            ["Requested"] = type.DefineField("flag", typeof(bool), FieldAttributes.Public | FieldAttributes.Static),
            ["Check"] = type.DefineMethod("check", MethodAttributes.Static, typeof(void), Type.EmptyTypes),
            ["BuildException"] = type.DefineMethod("factory", MethodAttributes.Static, typeof(Exception), Type.EmptyTypes),
        };
        var property = Handles.Single(p => p.Name == missing);
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        foreach (var other in Handles.Where(p => p != property)) other.SetValue(owner, declarations[other.Name]);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.False(owner.IsComplete);
        property.SetValue(owner, declarations[missing]);
        foreach (var handle in Handles)
        {
            Assert.Same(declarations[handle.Name], handle.GetValue(owner));
            Expect<InvalidOperationException>(() => handle.SetValue(owner, declarations[handle.Name]));
        }
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        owner.Check.GetILGenerator().Emit(OpCodes.Ret);
        owner.MarkCheckBodyEmitted();
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        var il = owner.BuildException.GetILGenerator(); il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Ret);
        owner.MarkBuildExceptionBodyEmitted();
        owner.CompleteEmission(); Assert.True(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Assert.Throws<InvalidOperationException>(owner.MarkCheckBodyEmitted);
        Assert.Throws<InvalidOperationException>(owner.MarkBuildExceptionBodyEmitted);
        foreach (var handle in Handles)
            Expect<InvalidOperationException>(() => handle.SetValue(owner, declarations[handle.Name]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BodyStagesAreIndependentAndForwardReferencesRemainUsable(bool factoryFirst)
    {
        var builder = NewAssembly(); var type = builder.DefineDynamicModule("main").DefineType("Forward", TypeAttributes.Public);
        var owner = new EmittedRuntime().Cancellation;
        Assert.Throws<InvalidOperationException>(owner.MarkCheckBodyEmitted);
        Assert.Throws<InvalidOperationException>(owner.MarkBuildExceptionBodyEmitted);
        owner.Requested = type.DefineField("flag", typeof(bool), FieldAttributes.Public | FieldAttributes.Static);
        owner.Check = type.DefineMethod("check", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        owner.BuildException = type.DefineMethod("factory", MethodAttributes.Public | MethodAttributes.Static, typeof(Exception), Type.EmptyTypes);
        var forward = owner.BuildException;
        var consumer = type.DefineMethod("Consumer", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        var il = consumer.GetILGenerator(); il.Emit(OpCodes.Call, forward); il.Emit(OpCodes.Throw);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        Action check = () => { owner.Check.GetILGenerator().Emit(OpCodes.Ret); owner.MarkCheckBodyEmitted(); };
        Action factory = () =>
        {
            var body = forward.GetILGenerator(); body.Emit(OpCodes.Newobj, typeof(OperationCanceledException).GetConstructor(Type.EmptyTypes)!);
            body.Emit(OpCodes.Ret); owner.MarkBuildExceptionBodyEmitted();
        };
        (factoryFirst ? factory : check)();
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        (factoryFirst ? check : factory)();
        Assert.Throws<InvalidOperationException>(owner.MarkCheckBodyEmitted);
        Assert.Throws<InvalidOperationException>(owner.MarkBuildExceptionBodyEmitted);
        owner.CompleteEmission(); Assert.Same(forward, owner.BuildException);
        type.CreateType(); var loaded = SaveVerifyLoad(builder);
        Expect<OperationCanceledException>(() => loaded.GetType("Forward")!.GetMethod("Consumer")!.Invoke(null, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterPreservesPublicContractIsolationAndInvocationChecks(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedCancellationRuntime>(); var handles = new HashSet<MemberInfo>();
        var flags = new List<FieldInfo>();
        foreach (string source in new[] { "const n=1;", "Promise.resolve(1); new Map(); new Set();", "const n=2;" })
        {
            var builder = NewAssembly();
            var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), features); var owner = runtime.Cancellation;
            Assert.True(owner.IsComplete); Assert.True(owners.Add(owner));
            foreach (var property in Handles)
            {
                var member = (MemberInfo)property.GetValue(owner)!;
                Assert.True(handles.Add(member)); Assert.Same(builder, member.Module.Assembly);
                Assert.Same(runtime.RuntimeType, member.DeclaringType);
            }
            var loaded = SaveVerifyLoad(builder); var type = loaded.GetType("$Runtime")!;
            var flag = type.GetField("_cancelRequested")!;
            var check = type.GetMethod("CheckCancellation")!; var factory = type.GetMethod("BuildCancellationException")!;
            Assert.Equal(typeof(bool), flag.FieldType); Assert.True(flag.IsPublic && flag.IsStatic && !flag.IsInitOnly);
            Assert.Equal(typeof(void), check.ReturnType); Assert.Equal(typeof(Exception), factory.ReturnType);
            foreach (var method in new[] { check, factory }) { Assert.True(method.IsPublic && method.IsStatic); Assert.Empty(method.GetParameters()); }
            Assert.False((bool)flag.GetValue(null)!); check.Invoke(null, null);
            Assert.Equal("Compiled execution cancelled.", Assert.IsType<OperationCanceledException>(factory.Invoke(null, null)).Message);
            Assert.NotSame(factory.Invoke(null, null), factory.Invoke(null, null));
            flag.SetValue(null, true);
            var error = Assert.IsType<OperationCanceledException>(Assert.Throws<TargetInvocationException>(() => check.Invoke(null, null)).InnerException);
            Assert.Equal("Compiled execution cancelled.", error.Message);
            Func<object[], object> callee = _ => "ok";
            foreach (var method in new[] { runtime.Invocation.Value, runtime.Invocation.Method, runtime.Invocation.Method0 })
            {
                object?[] args = method == runtime.Invocation.Value ? [callee, Array.Empty<object>()]
                    : method == runtime.Invocation.Method ? [null, callee, Array.Empty<object>()] : [null, callee];
                Expect<OperationCanceledException>(() => type.GetMethod(method.Name)!.Invoke(null, args));
            }
            // Early event-loop emission deliberately has no cancellation method dependency.
            var eventLoop = loaded.GetType(runtime.EventLoop.Type.Name)!;
            foreach (string method in new[] { "Run", "WaitForTask" })
                Assert.DoesNotContain(ReadInstructions(eventLoop.GetMethod(method)!), i => i.Member?.Name == "CheckCancellation");
            Assert.All(flags, old => Assert.True((bool)old.GetValue(null)!)); flags.Add(flag);
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EventLoopUsesOnlyItsExplicitCancellationDependency(bool supplied)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); var owner = new EmittedRuntime().Cancellation;
        var type = module.DefineType("Cancel", TypeAttributes.Public);
        foreach (string helper in new[] { "DefineCancellationFlag", "EmitCancellationCheck", "EmitCancellationExceptionFactory" })
            typeof(RuntimeEmitter).GetMethod(helper, Members)!.Invoke(emitter, [type, owner]);
        owner.CompleteEmission(); type.CreateType();
        var loop = new EmittedEventLoopRuntime();
        typeof(RuntimeEmitter).GetMethod("EmitTSEventLoopClass", Members)!.Invoke(emitter, [module, loop, supplied ? owner.Check : null]);
        var loaded = SaveVerifyLoad(builder); var loopType = loaded.GetType(loop.Type.Name)!;
        var instance = loopType.GetMethod("GetInstance")!.Invoke(null, null);
        loaded.GetType("Cancel")!.GetField("_cancelRequested")!.SetValue(null, true);
        foreach (string name in new[] { "Run", "WaitForTask" })
        {
            var method = loopType.GetMethod(name)!;
            Assert.Equal(supplied ? name == "Run" ? 2 : 1 : 0, ReadInstructions(method).Count(i => i.Member?.Name == "CheckCancellation"));
            if (supplied)
                Expect<OperationCanceledException>(() => method.Invoke(instance, name == "Run" ? null : [new TaskCompletionSource().Task]));
            else if (name == "Run") method.Invoke(instance, null);
            else Assert.True((bool)method.Invoke(instance, [Task.CompletedTask])!);
        }
    }

    [Theory]
    [InlineData("function run(n:number):number { let x=0; for(let i=0;i<n;i++){x+=i;} return x; }")]
    [InlineData("function run(n:number):number { let x=0; while(x<n){x++;} return x; }")]
    [InlineData("function run(n:number):number { let x=0; do{x++;}while(x<n); return x; }")]
    [InlineData("function run(n:number):number { let x=0; for(const v of [1,2,3]){x+=v;} return x; }")]
    public void GuestLoopsRetainVolatilePollAndNonReturningThrow(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var map = new TypeChecker().Check(statements); var compiler = new ILCompiler($"cancel_loop_{Guid.NewGuid():N}");
        compiler.Compile(statements, map, new DeadCodeAnalyzer(map).Analyze(statements));
        var bytes = compiler.SaveToBytes(); using var stream = new MemoryStream(bytes);
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]); Assert.Empty(verifier.Verify(stream));
        var loaded = Assembly.Load(bytes);
        var method = loaded.GetType("$Program")!.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(m => m.Name.EndsWith("run", StringComparison.Ordinal));
        var instructions = ReadInstructions(method).ToArray();
        var polls = instructions.Select((i, index) => (i, index)).Where(x => x.i.Member?.Name == "_cancelRequested").ToArray();
        Assert.NotEmpty(polls);
        foreach (var poll in polls) Assert.Equal(OpCodes.Volatile, instructions[poll.index - 1].Code);
        var factories = instructions.Select((i, index) => (i, index)).Where(x => x.i.Member?.Name == "BuildCancellationException").ToArray();
        Assert.NotEmpty(factories);
        foreach (var factory in factories) Assert.Equal(OpCodes.Throw, instructions[factory.index + 1].Code);
        Assert.DoesNotContain(instructions, i => i.Member?.Name == "CheckCancellation");
        var run = method.CreateDelegate<Func<double, double>>(); var expected = run(4);
        var flag = loaded.GetType("$Runtime")!.GetField("_cancelRequested")!;
        flag.SetValue(null, true); Assert.Throws<OperationCanceledException>(() => run(4));
        flag.SetValue(null, false); Assert.Equal(expected, run(4));
    }

    [Fact]
    public void OwnerAndHelpersHaveNoFlatAliasesOrWholeRuntimeDependencies()
    {
        Assert.Equal(3, Handles.Length);
        Assert.NotSame(new EmittedRuntime().Cancellation, new EmittedRuntime().Cancellation);
        Assert.Null(typeof(EmittedRuntime).GetProperty("Cancellation")!.SetMethod);
        foreach (string name in new[] { "CancelRequestedField", "CheckCancellationMethod", "BuildCancellationExceptionMethod" })
            Assert.Null(typeof(EmittedRuntime).GetProperty(name));
        foreach (string name in new[] { "DefineCancellationFlag", "EmitCancellationCheck", "EmitCancellationExceptionFactory", "EmitTSEventLoopClass", "EmitEventLoopRun", "EmitEventLoopWaitForTask" })
            Assert.DoesNotContain(typeof(RuntimeEmitter).GetMethod(name, Members)!.GetParameters(),
                p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
    }

    private static IEnumerable<(OpCode Code, MemberInfo? Member)> ReadInstructions(MethodInfo method)
    {
        var codes = typeof(OpCodes).GetFields(BindingFlags.Static | BindingFlags.Public)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(c => c.Value);
        var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
        for (int offset = 0; offset < bytes.Length;)
        {
            byte first = bytes[offset++];
            var code = codes[first == 0xfe ? unchecked((short)(0xfe00 | bytes[offset++])) : first];
            MemberInfo? member = code.OperandType switch
            {
                OperandType.InlineMethod => method.Module.ResolveMethod(BitConverter.ToInt32(bytes, offset)),
                OperandType.InlineField => method.Module.ResolveField(BitConverter.ToInt32(bytes, offset)),
                _ => null,
            };
            offset += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineI or OperandType.ShortInlineVar or OperandType.ShortInlineBrTarget => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                _ => 4,
            };
            yield return (code, member);
        }
    }

    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"cancellation_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes)); return Assembly.Load(bytes.ToArray());
    }
}
