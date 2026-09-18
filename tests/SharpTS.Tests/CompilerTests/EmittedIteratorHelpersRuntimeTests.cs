using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedIteratorHelpersRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static PropertyInfo[] Slots => typeof(EmittedIteratorHelpersRuntime).GetProperties().Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType)).ToArray();
    public static IEnumerable<object[]> SlotNames => Slots.Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(SlotNames))]
    public void MissingDeclarationCanBeRepairedAndCompletionFreezesMetadata(string missing)
    {
        var owner = new EmittedRuntime().IteratorHelpers;
        var type = NewAssembly().DefineDynamicModule("main").DefineType("Handles");
        var method = type.DefineMethod("Method", MethodAttributes.Public | MethodAttributes.Static, typeof(object), Type.EmptyTypes);
        var ctor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
        object Handle(PropertyInfo slot) => slot.PropertyType == typeof(ConstructorBuilder) ? ctor : method;
        var property = Slots.Single(p => p.Name == missing);
        Expect<InvalidOperationException>(() => property.GetValue(owner));
        Expect<ArgumentNullException>(() => property.SetValue(owner, null));
        foreach (var other in Slots.Where(p => p.Name != missing)) other.SetValue(owner, Handle(other));
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission); Assert.False(owner.IsComplete);
        property.SetValue(owner, Handle(property)); Assert.Same(Handle(property), property.GetValue(owner));
        Expect<InvalidOperationException>(() => property.SetValue(owner, Handle(property)));
        owner.CompleteEmission(); Assert.True(owner.IsComplete);
        Assert.Throws<InvalidOperationException>(owner.CompleteEmission);
        foreach (var slot in Slots) Expect<InvalidOperationException>(() => slot.SetValue(owner, Handle(slot)));
    }

    [Fact]
    public void RequiredOwnerIsFreshForEachCompilation()
    {
        var first = new EmittedRuntime(); var second = new EmittedRuntime();
        Assert.NotSame(first.IteratorHelpers, second.IteratorHelpers);
        Assert.False(first.IteratorHelpers.IsComplete); Assert.False(second.IteratorHelpers.IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterPreservesHelperAbiLazinessAndOutputIsolation(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedIteratorHelpersRuntime>();
        foreach (string source in new[] { "const n=1;", "Buffer.from('x');new Uint8Array(2);function* g(){yield 1;}g();", "const n=2;" })
        {
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), Detect(source));
            Assert.True(owners.Add(runtime.IteratorHelpers)); Assert.True(runtime.IteratorHelpers.IsComplete);
            foreach (var slot in Slots) Assert.Same(builder, ((MemberInfo)slot.GetValue(runtime.IteratorHelpers)!).Module.Assembly);
            var loaded = SaveVerifyLoad(builder); var type = loaded.GetType("$Runtime")!;
            foreach (var slot in Slots)
            {
                var handle = (MemberInfo)slot.GetValue(runtime.IteratorHelpers)!;
                if (handle is ConstructorInfo)
                {
                    var ctor = Assert.Single(loaded.GetType(handle.DeclaringType!.FullName!)!.GetConstructors());
                    Assert.Equal(new[] { typeof(IEnumerator<object>), slot.Name is "TakeIteratorCtor" or "DropIteratorCtor" ? typeof(int) : typeof(object) }, ctor.GetParameters().Select(p => p.ParameterType));
                    continue;
                }
                var method = type.GetMethod(handle.Name)!; Assert.True(method.IsPublic && method.IsStatic);
                Assert.Equal(slot.Name == "NormalizeToEnumerator" ? typeof(IEnumerator<object>) : typeof(object), method.ReturnType);
                Type[] expected = slot.Name switch
                {
                    "NormalizeToEnumerator" or "ToArray" or "From" => [typeof(object)],
                    "Take" or "Drop" => [typeof(object), typeof(int)],
                    "Reduce" => [typeof(object), typeof(object), typeof(object), typeof(bool)],
                    _ => [typeof(object), typeof(object)]
                };
                Assert.Equal(expected, method.GetParameters().Select(p => p.ParameterType));
            }
            object? Call(string name, params object?[] args) => type.GetMethod(name)!.Invoke(null, args);
            List<object> Values(object input) => (List<object>)Call("IteratorToArray", input)!;
            var values = new List<object> { 1d, 2d, 3d }; int calls = 0;
            var map = new Func<object[], object>(args => { calls++; return (double)args[0] * 2 + (double)args[1]; });
            var mapped = Call("IteratorMap", values, map)!; Assert.Equal(0, calls);
            Assert.Equal(new object[] { 2d, 5d, 8d }, Values(mapped)); Assert.Equal(3, calls);
            var predicate = new Func<object[], object>(args => (double)args[0] > 1);
            Assert.Equal(new object[] { 2d, 3d }, Values(Call("IteratorFilter", values, predicate)!));
            Assert.Equal(new object[] { 1d, 2d }, Values(Call("IteratorTake", values, 2)!));
            Assert.Equal(new object[] { 3d }, Values(Call("IteratorDrop", values, 2)!));
            Assert.Equal(new object[] { 1d, 1d, 2d, 2d, 3d, 3d }, Values(Call("IteratorFlatMap", values, new Func<object[], object>(args => new List<object> { args[0], args[0] }))!));
            var sum = new Func<object[], object>(args => (double)args[0] + (double)args[1]);
            Assert.Equal(6d, Call("IteratorReduce", values, sum, null, false));
            Assert.Equal(16d, Call("IteratorReduce", values, sum, 10d, true));
            Assert.Contains("Reduce of empty iterator", Assert.Throws<TargetInvocationException>(() => Call("IteratorReduce", new List<object>(), sum, null, false)).InnerException!.Message);
            double total = 0; Call("IteratorForEach", values, new Func<object[], object>(args => total += (double)args[0])); Assert.Equal(6d, total);
            Assert.Equal(true, Call("IteratorSome", values, predicate)); Assert.Equal(false, Call("IteratorEvery", values, predicate));
            Assert.Equal(2d, Call("IteratorFind", values, predicate)); Assert.Same(values, Call("IteratorFrom", values));
            var iterator = (IEnumerator<object>)Call("NormalizeToEnumerator", values)!;
            Assert.Same(iterator, Call("NormalizeToEnumerator", iterator));
            var next = (Dictionary<string, object>)Call("IteratorNext", iterator, null)!; Assert.Equal(1d, next["value"]); Assert.Equal(false, next["done"]);
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CallbackMoveNextUsesSuppliedInvocationAndTruthiness(bool filter, bool truthy)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var type = module.DefineType("Probe", TypeAttributes.Public, typeof(object), [typeof(IEnumerator<object>), typeof(IEnumerator), typeof(IDisposable)]);
        var source = type.DefineField("source", typeof(IEnumerator<object>), FieldAttributes.Public);
        var callbackField = type.DefineField("callback", typeof(object), FieldAttributes.Public);
        var index = type.DefineField("index", typeof(int), FieldAttributes.Public);
        var current = type.DefineField("current", typeof(object), FieldAttributes.Public);
        type.DefineDefaultConstructor(MethodAttributes.Public);
        var invoke = type.DefineMethod("Invoke", MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object), typeof(object), typeof(object[])]);
        var il = invoke.GetILGenerator(); il.Emit(OpCodes.Ldarg_2); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ldelem_Ref); il.Emit(OpCodes.Unbox_Any, typeof(double)); il.Emit(OpCodes.Ldc_R8, 10d); il.Emit(OpCodes.Add); il.Emit(OpCodes.Box, typeof(double)); il.Emit(OpCodes.Ret);
        var test = type.DefineMethod("Truthy", MethodAttributes.Public | MethodAttributes.Static, typeof(bool), [typeof(object)]);
        il = test.GetILGenerator(); il.Emit(truthy ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); var helper = typeof(RuntimeEmitter).GetMethod("EmitCallbackMoveNext", Members)!;
        var inputs = Activator.CreateInstance(helper.GetParameters()[1].ParameterType, invoke, test)!;
        helper.Invoke(emitter, [type, inputs, source, callbackField, index, current, true, filter]);
        typeof(RuntimeEmitter).GetMethod("EmitCurrentProperty", Members)!.Invoke(emitter, [type, current]);
        typeof(RuntimeEmitter).GetMethod("EmitResetThrows", Members)!.Invoke(emitter, [type]);
        typeof(RuntimeEmitter).GetMethod("EmitDisposeNoOp", Members)!.Invoke(emitter, [type]);
        type.CreateType(); var loaded = SaveVerifyLoad(builder); var probe = loaded.GetType("Probe")!;
        var instance = (IEnumerator<object>)Activator.CreateInstance(probe)!;
        probe.GetField("source")!.SetValue(instance, new List<object> { 1d, 2d }.GetEnumerator());
        probe.GetField("callback")!.SetValue(instance, new object()); var actual = new List<object>();
        while (instance.MoveNext()) actual.Add(instance.Current);
        Assert.Equal(filter ? (truthy ? new object[] { 1d, 2d } : []) : new object[] { 10d, 11d }, actual);
        Assert.Equal(2, probe.GetField("index")!.GetValue(instance));
        Assert.Throws<NotSupportedException>(instance.Reset); instance.Dispose();
    }

    [Fact]
    public void ScopedHelpersDoNotRetainTheWholeHolder()
    {
        Assert.Equal(19, Slots.Length);
        string[] methods = ["EmitIteratorHelperMethods", "EmitNormalizeToEnumerator", "EmitMapIteratorType", "EmitFilterIteratorType", "EmitTakeIteratorType", "EmitDropIteratorType", "EmitFlatMapIteratorType", "EmitCallbackMoveNext", "EmitIteratorMap", "EmitIteratorFilter", "EmitIteratorTake", "EmitIteratorDrop", "EmitIteratorFlatMap", "EmitIteratorReduce", "EmitIteratorToArray", "EmitIteratorForEach", "EmitIteratorSome", "EmitIteratorEvery", "EmitIteratorPredicateMethod", "EmitIteratorFind", "EmitIteratorNext", "EmitIteratorFrom"];
        foreach (var slot in Slots) Assert.Null(typeof(EmittedRuntime).GetProperty(slot.Name.EndsWith("Ctor") || slot.Name == "NormalizeToEnumerator" ? slot.Name : "Iterator" + slot.Name));
        foreach (string name in methods)
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, Members)!;
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
            foreach (var input in method.GetParameters().Where(p => p.Name is "inputs" or "callback"))
                Assert.DoesNotContain(input.ParameterType.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
        }
    }

    private static RuntimeFeatureSet Detect(string source) => new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"iterator_helpers_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        var errors = verifier.Verify(bytes); Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors)); return Assembly.Load(bytes.ToArray());
    }
}
