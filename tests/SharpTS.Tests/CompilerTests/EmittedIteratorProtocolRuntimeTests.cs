using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class EmittedIteratorProtocolRuntimeTests
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static PropertyInfo[] Slots => typeof(EmittedIteratorProtocolRuntime).GetProperties().Where(p => typeof(MemberInfo).IsAssignableFrom(p.PropertyType)).ToArray();

    [Theory]
    [InlineData("Function")]
    [InlineData("InvokeNext")]
    [InlineData("Done")]
    [InlineData("Value")]
    [InlineData("Close")]
    [InlineData("Call")]
    public void MissingDeclarationCanBeRepairedAndCompletionFreezesMetadata(string missing)
    {
        var owner = new EmittedRuntime().IteratorProtocol;
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
        Assert.NotSame(first.IteratorProtocol, second.IteratorProtocol);
        Assert.False(first.IteratorProtocol.IsComplete); Assert.False(second.IteratorProtocol.IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedEmitterPreservesProtocolResultsCloseAndHelperAbi(bool hosted)
    {
        var emitter = new RuntimeEmitter(TypeProvider.Runtime, emitHosted: hosted);
        var owners = new HashSet<EmittedIteratorProtocolRuntime>();
        foreach (var (source, streams) in new[]
        {
            ("const n=1;", false),
            ("function* values(){yield 1;}for(const n of values()){}", false),
            ("import {Readable} from 'node:stream';const s=Readable.from([1]);", true),
            ("async function run(){for await(const n of [1,2]){}}run();", false),
            ("const n=2;", false)
        })
        {
            var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer(source).ScanTokens()).ParseOrThrow());
            var builder = NewAssembly(); var runtime = emitter.EmitAll(builder.DefineDynamicModule("main"), features);
            Assert.Equal(streams, runtime.NodeStreams is not null);
            Assert.True(owners.Add(runtime.IteratorProtocol)); Assert.True(runtime.IteratorProtocol.IsComplete);
            foreach (var slot in Slots) Assert.Same(builder, ((MemberInfo)slot.GetValue(runtime.IteratorProtocol)!).Module.Assembly);
            var loaded = SaveVerifyLoad(builder); var runtimeType = loaded.GetType("$Runtime")!;
            var symbolType = loaded.GetType(runtime.Symbols.Type.FullName!)!;
            foreach (var (name, result, args) in new[]
            {
                ("GetIteratorFunction", typeof(object), new[] { typeof(object), symbolType }),
                ("InvokeIteratorNext", typeof(object), new[] { typeof(object) }),
                ("InvokeIteratorNextWithSent", typeof(object), new[] { typeof(object), typeof(object) }),
                ("GetIteratorDone", typeof(bool), new[] { typeof(object) }),
                ("GetIteratorValue", typeof(object), new[] { typeof(object) }),
                ("IteratorClose", typeof(void), new[] { typeof(object), typeof(bool) }),
                ("IteratorProtocolCall", typeof(object), new[] { typeof(object), typeof(string), typeof(object[]) })
            })
            {
                var method = runtimeType.GetMethod(name)!;
                Assert.True(method.IsPublic && method.IsStatic); Assert.Equal(result, method.ReturnType);
                Assert.Equal(args, method.GetParameters().Select(p => p.ParameterType));
            }
            object? Call(string name, params object?[] args) => runtimeType.GetMethod(name)!.Invoke(null, args);
            var resultRecord = new Dictionary<string, object> { ["value"] = 12d, ["done"] = "truthy" };
            Assert.Equal(true, Call("GetIteratorDone", resultRecord)); Assert.Equal(12d, Call("GetIteratorValue", resultRecord));
            resultRecord["done"] = 0d; Assert.Equal(false, Call("GetIteratorDone", resultRecord));
            var iterator = new Dictionary<string, object>
            {
                ["next"] = new Func<object[], object>(args => args.Length == 0 ? 10d : args[0]),
                ["return"] = new Func<object[], object>(_ => new Dictionary<string, object>())
            };
            Assert.Equal(10d, Call("InvokeIteratorNext", iterator)); Assert.Equal(9d, Call("InvokeIteratorNextWithSent", iterator, 9d));
            Call("IteratorClose", iterator, false);
            iterator["return"] = new Func<object[], object>(_ => 3d);
            Assert.Contains("Iterator .return() must return an object", Assert.Throws<TargetInvocationException>(() => Call("IteratorClose", iterator, false)).InnerException!.Message);
            Call("IteratorClose", iterator, true);
            var enumerator = ((IEnumerable<object>)new object[] { 4d, 5d }).GetEnumerator();
            var next = Assert.IsType<Dictionary<string, object>>(Call("IteratorProtocolCall", enumerator, "next", Array.Empty<object>()));
            Assert.Equal(4d, next["value"]); Assert.Equal(false, next["done"]);
            var returned = Assert.IsType<Dictionary<string, object>>(Call("IteratorProtocolCall", enumerator, "return", new object[] { 9d }));
            Assert.Equal(9d, returned["value"]); Assert.Equal(true, returned["done"]);
            Assert.DoesNotContain(loaded.GetReferencedAssemblies(), a => a.Name == "SharpTS");
            Assert.Equal(hosted, loaded.GetReferencedAssemblies().Any(a => a.Name == "SharpTS.Hosting.Abstractions"));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CompactReaderUsesSuppliedShapesAndDescriptorSelection(bool selected, bool descriptors)
    {
        var builder = NewAssembly(); var module = builder.DefineDynamicModule("main");
        var carrier = module.DefineType("Carrier", TypeAttributes.Public); carrier.DefineDefaultConstructor(MethodAttributes.Public);
        var valueField = carrier.DefineField("Value", typeof(double), FieldAttributes.Public);
        var doneField = carrier.DefineField("Done", typeof(bool), FieldAttributes.Public);
        var materializedField = carrier.DefineField("Materialized", typeof(bool), FieldAttributes.Public);
        var descriptorsField = carrier.DefineField("Descriptors", typeof(bool), FieldAttributes.Public);
        var materialized = carrier.DefineMethod("IsMaterialized", MethodAttributes.Public, typeof(bool), Type.EmptyTypes);
        var il = materialized.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, materializedField); il.Emit(OpCodes.Ret);
        var helper = module.DefineType("Helpers", TypeAttributes.Public);
        var hasDescriptors = helper.DefineMethod("HasDescriptors", MethodAttributes.Public | MethodAttributes.Static, typeof(bool), [typeof(object)]);
        il = hasDescriptors.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Castclass, carrier); il.Emit(OpCodes.Ldfld, descriptorsField); il.Emit(OpCodes.Ret);
        var runtime = new EmittedRuntime(); runtime.DescriptorStorage.HasPropertyDescriptors = hasDescriptors;
        runtime.Records.BeginScalarEmission();
        runtime.Records.AddCompactTypes("shape", carrier, 2);
        runtime.Records.AddCompactValueFields(("shape", 0), valueField); runtime.Records.AddCompactValueFields(("shape", 1), doneField);
        runtime.Records.AddCompactIsMaterializedGetters("shape", materialized);
        var shape = new JsonSerializationShape.Record([("value", new JsonSerializationShape.Number()), ("done", new JsonSerializationShape.Boolean())]);
        var shapes = new Dictionary<string, JsonSerializationShape.Record> { ["shape"] = shape };
        var features = new RuntimeFeatureSet { UsesDynamicPropertyDescriptors = !descriptors };
        if (!selected) features.CompactObjectRecordIteratorResultShapes.Add("shape");
        features.CompactObjectRecordShapes.Add("shape", new JsonSerializationShape.Record([("done", new JsonSerializationShape.Boolean()), ("value", new JsonSerializationShape.Number())]));
        var emitter = new RuntimeEmitter(TypeProvider.Runtime); typeof(RuntimeEmitter).GetField("_features", Members)!.SetValue(emitter, features);
        var emit = typeof(RuntimeEmitter).GetMethod("EmitCompactIteratorResultRead", Members)!;
        var inputType = emit.GetParameters()[1].ParameterType;
        var inputs = Activator.CreateInstance(inputType, runtime.DescriptorStorage, runtime.Records, selected ? new HashSet<string> { "shape" } : new HashSet<string>(), shapes, descriptors)!;
        foreach (string key in new[] { "value", "done" })
        {
            var method = helper.DefineMethod(key, MethodAttributes.Public | MethodAttributes.Static, key == "value" ? typeof(object) : typeof(bool), [typeof(object)]);
            il = method.GetILGenerator(); emit.Invoke(emitter, [il, inputs, key]);
            if (key == "value") { il.Emit(OpCodes.Ldc_R8, -1d); il.Emit(OpCodes.Box, typeof(double)); }
            else il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
        }
        carrier.CreateType(); helper.CreateType(); var loaded = SaveVerifyLoad(builder);
        var carrierType = loaded.GetType("Carrier")!; var instance = Activator.CreateInstance(carrierType)!;
        carrierType.GetField("Value")!.SetValue(instance, 12d); carrierType.GetField("Done")!.SetValue(instance, true);
        object? Read(string key, object receiver) => loaded.GetType("Helpers")!.GetMethod(key)!.Invoke(null, [receiver]);
        Assert.Equal(selected ? 12d : -1d, Read("value", instance)); Assert.Equal(selected, Read("done", instance));
        Assert.Equal(-1d, Read("value", new object())); Assert.Equal(false, Read("done", new object()));
        carrierType.GetField("Descriptors")!.SetValue(instance, true);
        Assert.Equal(selected && !descriptors ? 12d : -1d, Read("value", instance)); Assert.Equal(selected && !descriptors, Read("done", instance));
        carrierType.GetField("Descriptors")!.SetValue(instance, false); carrierType.GetField("Materialized")!.SetValue(instance, true);
        Assert.Equal(-1d, Read("value", instance)); Assert.Equal(false, Read("done", instance));
    }

    [Fact]
    public void ProtocolHelpersUseExactInputsAndUnusedStoreIsRemoved()
    {
        Assert.Equal(6, Slots.Length); Assert.Null(typeof(EmittedRuntime).GetProperty("InvokeIteratorNextWithSent"));
        foreach (string name in new[] { "EmitGetIteratorFunction", "EmitInvokeIteratorNext", "EmitInvokeIteratorNextWithSent", "EmitGetIteratorDone", "EmitGetIteratorValue", "EmitIteratorClose", "EmitIteratorProtocolCall", "EmitArgZeroOrUndefined", "EmitCompactIteratorResultRead" })
        {
            var method = typeof(RuntimeEmitter).GetMethod(name, Members)!;
            Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
            foreach (var input in method.GetParameters().Where(p => p.Name == "inputs"))
                Assert.DoesNotContain(input.ParameterType.GetFields(Members), f => f.FieldType == typeof(EmittedRuntime) || f.FieldType == typeof(RuntimeFeatureSet));
        }
    }

    private static void Expect<T>(Action action) where T : Exception => Assert.IsType<T>(Assert.Throws<TargetInvocationException>(action).InnerException);
    private static PersistedAssemblyBuilder NewAssembly() => new(new AssemblyName($"iterator_protocol_{Guid.NewGuid():N}"), typeof(object).Assembly);
    private static Assembly SaveVerifyLoad(PersistedAssemblyBuilder builder)
    {
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes)); return Assembly.Load(bytes.ToArray());
    }
}
