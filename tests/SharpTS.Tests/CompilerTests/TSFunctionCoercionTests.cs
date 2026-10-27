using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class TSFunctionCoercionTests
{
    [Fact]
    public void StagedCoercionUsesSuppliedPeerMethodsWithoutARuntimeType()
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName("coercion_peers_" + Guid.NewGuid().ToString("N")), typeof(object).Assembly);
        var type = builder.DefineDynamicModule("main").DefineType("SelectedPeers", TypeAttributes.Public);
        var guardCalls = type.DefineField("GuardCalls", typeof(int), FieldAttributes.Public | FieldAttributes.Static);
        MethodBuilder Peer(string name, Type result) => type.DefineMethod(name,
            MethodAttributes.Public | MethodAttributes.Static, result, [typeof(object)]);
        var text = Peer("SelectedText", typeof(string));
        var il = text.GetILGenerator(); il.Emit(OpCodes.Ldstr, "selected"); il.Emit(OpCodes.Ret);
        var number = Peer("SelectedNumber", typeof(double));
        il = number.GetILGenerator(); il.Emit(OpCodes.Ldc_R8, 12.75); il.Emit(OpCodes.Ret);
        var list = Peer("SelectedList", typeof(List<object>));
        il = list.GetILGenerator(); il.Emit(OpCodes.Newobj, typeof(List<object>).GetConstructor(Type.EmptyTypes)!);
        il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldstr, "selected list");
        il.Emit(OpCodes.Callvirt, typeof(List<object>).GetMethod(nameof(List<object>.Add))!); il.Emit(OpCodes.Ret);
        var guard = Peer("SelectedGuard", typeof(object));
        il = guard.GetILGenerator(); il.Emit(OpCodes.Ldsfld, guardCalls); il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add); il.Emit(OpCodes.Stsfld, guardCalls); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);

        const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Instance;
        var inputType = typeof(RuntimeEmitter).GetNestedType("TSFunctionCoercionInputs", BindingFlags.NonPublic)!;
        var inputs = Activator.CreateInstance(inputType, text, number, list, guard)!;
        typeof(RuntimeEmitter).GetMethod("EmitTSFunctionCoercePrimitivesHelper", hidden)!
            .Invoke(new RuntimeEmitter(TypeProvider.Runtime), [type, typeof(Uri), inputs]);
        type.CreateType();
        using var bytes = new MemoryStream(); builder.Save(bytes); bytes.Position = 0;
        using var verifier = new ILVerifier(extraProbeDirectories: [AppContext.BaseDirectory]);
        Assert.Empty(verifier.Verify(bytes));
        var loaded = Assembly.Load(bytes.ToArray()).GetType(type.Name)!;
        var coerce = loaded.GetMethod("CoercePrimitiveArgs", BindingFlags.NonPublic | BindingFlags.Static)!;
        object[] arguments = [new object(), new object(), new object(), new object()];
        coerce.Invoke(null, [typeof(TSFunctionCoercionTests).GetMethod(nameof(ParameterShape), BindingFlags.NonPublic | BindingFlags.Static)!.GetParameters(), arguments, false]);
        Assert.Equal("selected", arguments[0]); Assert.Equal(12.75, arguments[1]); Assert.Equal(12, arguments[2]);
        Assert.Equal("selected list", Assert.Single(Assert.IsType<List<object>>(arguments[3])));
        Assert.Equal(0, loaded.GetField("GuardCalls")!.GetValue(null));
        arguments = [new object()];
        coerce.Invoke(null, [typeof(TSFunctionCoercionTests).GetMethod(nameof(ReceiverShape), BindingFlags.NonPublic | BindingFlags.Static)!.GetParameters(), arguments, true]);
        Assert.Equal("selected", arguments[0]);
        Assert.Equal(1, loaded.GetField("GuardCalls")!.GetValue(null));
    }

    internal static void ParameterShape(string text, double number, int integer, List<object> array) { }
    internal static void ReceiverShape(string __this) { }

    [Fact]
    public void PrimitiveCoercionCallsExactGuestHelpersWithoutReflectionDiscovery()
    {
        var (assembly, output) = TestHarness.CompileAndRun("console.log(1);", DecoratorMode.None);
        Assert.Equal("1\n", output);
        var runtime = assembly.GetType("$Runtime")!;
        var wrapper = assembly.GetType("$TSFunction")!;
        var coerce = wrapper.GetMethod("CoercePrimitiveArgs", BindingFlags.NonPublic | BindingFlags.Static)!;
        var calls = CalledMethods(coerce).ToArray();
        foreach (string name in new[] { "ToJsString", "ToNumber", "ArrayLikeMaterialize", "RequireObjectCoercibleThis" })
            Assert.Contains(runtime.GetMethod(name)!, calls);
        Assert.DoesNotContain(calls, method => method.DeclaringType == typeof(Assembly)
            || method.DeclaringType == typeof(MethodBase) && method.Name == nameof(MethodBase.Invoke)
            || method.DeclaringType == typeof(Type) && method.Name == nameof(Type.GetMethod));
        foreach (string name in new[] { "_toJsStringCache", "_toNumberCache", "_arrayLikeMaterializeCache", "_requireObjectCoercibleThisCache" })
            Assert.Null(wrapper.GetField(name, BindingFlags.NonPublic | BindingFlags.Static));
    }

    internal static IEnumerable<MethodBase> CalledMethods(MethodInfo method)
    {
        var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
        for (int offset = 0; offset < bytes.Length;)
        {
            byte first = bytes[offset++];
            short value = first == 0xfe ? unchecked((short)(0xfe00 | bytes[offset++])) : first;
            var code = OpCodesByValue[value];
            if (code.OperandType == OperandType.InlineMethod)
                yield return method.Module.ResolveMethod(BitConverter.ToInt32(bytes, offset))!;
            offset += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineI or OperandType.ShortInlineVar or OperandType.ShortInlineBrTarget => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                _ => 4,
            };
        }
    }

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(code => code.Value);
}
