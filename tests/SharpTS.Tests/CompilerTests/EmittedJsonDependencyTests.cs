using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class EmittedJsonDependencyTests
{
    public static IEnumerable<object[]> RegExpSelections => new[]
    {
        "EmitAppendJsonValueHelper", "EmitJsonStringifyHelper", "EmitStringifyValueFullHelper"
    }.SelectMany(name => Enumerable.Range(0, 4).Select(mask =>
        new object[] { name, (mask & 1) != 0, (mask & 2) != 0 }));

    [Theory]
    [MemberData(nameof(RegExpSelections))]
    public void RegExpOperandFollowsProvidedTypeRegardlessOfGlobalFlag(string helper, bool supplied, bool globalFlag)
    {
        var features = new RuntimeFeatureDetector().Detect(new Parser(new Lexer(
            "console.log(JSON.stringify(/x/));").ScanTokens()).ParseOrThrow());
        var builder = new PersistedAssemblyBuilder(new AssemblyName($"json_dependency_{Guid.NewGuid():N}"), typeof(object).Assembly);
        var module = builder.DefineDynamicModule("main");
        var emitter = new RuntimeEmitter(TypeProvider.Runtime);
        var runtime = emitter.EmitAll(module, features);
        Assert.NotNull(runtime.RegExps.RequireImplementation().Type);
        features.UsesRegExp = globalFlag;
        var method = typeof(RuntimeEmitter).GetMethod(helper, BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.DoesNotContain(method.GetParameters(), p => p.ParameterType == typeof(EmittedRuntime) || p.ParameterType == typeof(RuntimeFeatureSet));
        var inputType = method.GetParameters()[2].ParameterType;
        var constructor = Assert.Single(inputType.GetConstructors());
        object?[] arguments = constructor.GetParameters().Select(parameter =>
        {
            if (parameter.Name == "TSRegExpType") return supplied ? runtime.RegExps.RequireImplementation().Type : null;
            if (parameter.Name == "TSSymbolType") return runtime.Symbols.Type;
            if (parameter.Name == "TypeOf") return runtime.Operators.TypeOf;
            if (parameter.Name == "GetProperty") return runtime.ObjectRead.Property;
            if (parameter.Name == "GetIndex") return runtime.ObjectRead.Index;
            if (parameter.Name == "GetKeys") return runtime.ObjectKeys.Keys;
            if (parameter.Name == "TSObjectMergeEnumerable") return runtime.ObjectConstruction.GetEnumerableFields;
            if (parameter.Name == "CreateException") return runtime.Errors.CreateException;
            if (parameter.Name == "TSTypeErrorCtor") return runtime.Errors.TypeErrorConstructor;
            if (parameter.Name == "BoundTSFunctionInvokeWithThis") return runtime.FunctionBindings.BoundInvokeWithThis;
            if (parameter.Name == "BoundTSFunctionType") return runtime.FunctionBindings.BoundType;
            if (parameter.Name == "TSFunctionInvokeWithThis") return runtime.FunctionValues.InvokeWithThis;
            if (parameter.Name == "TSFunctionType") return runtime.FunctionValues.Type;
            if (parameter.Name == "InvokeMethodUnwrapped") return runtime.ReflectedMethods.InvokeUnwrapped;
            if (parameter.Name == "InvokeValue") return runtime.Invocation.Value;
            if (parameter.Name == "InvokeMethodValue") return runtime.Invocation.Method;
            if (parameter.Name == "InvokeMethodValue0") return runtime.Invocation.Method0;
            if (parameter.Name == "IHasFieldsInterface") return runtime.ObjectFields.Interface;
            if (parameter.Name == "UndefinedType") return runtime.Sentinels.UndefinedType;
            if (parameter.Name == "UndefinedInstance") return runtime.Sentinels.UndefinedInstance;
            var property = typeof(EmittedRuntime).GetProperty(parameter.Name!);
            return property is not null ? property.GetValue(runtime)
                : typeof(RuntimeFeatureSet).GetProperty(parameter.Name!)!.GetValue(features);
        }).ToArray();
        object inputs = constructor.Invoke(arguments);
        var json = new EmittedJsonImplementation();
        foreach (var property in typeof(EmittedJsonImplementation).GetProperties()
            .Where(property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType) && property.Name != "AppendValue"))
            property.SetValue(json, property.GetValue(runtime.Json.RequireImplementation()));
        var type = module.DefineType("$JsonDependencyProbe", TypeAttributes.Public);
        var emitted = Assert.IsAssignableFrom<MethodBuilder>(method.Invoke(emitter, [type, json, inputs]));
        type.CreateType();
        using var bytes = new MemoryStream();
        builder.Save(bytes);
        // This fragment inspects compiler selection using existing helper handles.
        // Complete saved-runtime IL verification and guest execution live in EmittedJsonRuntimeTests.
        var assembly = Assembly.Load(bytes.ToArray());
        var saved = assembly.GetType(type.Name)!.GetMethod(emitted.Name, BindingFlags.Static | BindingFlags.NonPublic)!;
        var operands = ReadTypeOperands(saved).Where(item => item.OpCode == OpCodes.Isinst && item.Type.Name == "$RegExp").ToArray();
        Assert.Equal(supplied ? 1 : 0, operands.Length);
        if (supplied) Assert.Same(assembly.GetType(runtime.RegExps.RequireImplementation().Type.Name), operands[0].Type);
    }

    private static IEnumerable<(OpCode OpCode, Type Type)> ReadTypeOperands(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
        for (int offset = 0; offset < il.Length;)
        {
            byte first = il[offset++];
            short value = first == 0xfe ? unchecked((short)(0xfe00 | il[offset++])) : first;
            OpCode opCode = OpCodeByValue[value];
            if (opCode.OperandType == OperandType.InlineType)
                yield return (opCode, method.Module.ResolveType(BitConverter.ToInt32(il, offset)));
            offset += opCode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI or OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineMethod or
                    OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => throw new InvalidOperationException($"Unsupported IL operand type {opCode.OperandType}.")
            };
        }
    }

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodeByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(opCode => opCode.Value);
}
