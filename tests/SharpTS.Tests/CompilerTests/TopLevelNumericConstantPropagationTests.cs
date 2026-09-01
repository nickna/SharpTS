using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Regression coverage for numeric module constants used by compiled function hot paths. These
/// bindings remain fully materialized for module semantics, but immutable reads need not reload
/// and convert the boxed value on every iteration.
/// </summary>
public sealed class TopLevelNumericConstantPropagationTests
{
    private const string KernelSource = """
        const MODULUS: number = 1000000007;

        export function kernel(value: number): number {
            return value % MODULUS;
        }
        """;

    [Fact]
    public void CapturedNumericModuleConstant_EmitsNativeLiteralWithoutBoxedValueLoad()
    {
        Assembly assembly = CompileKernelModule();
        MethodInfo kernel = assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.EndsWith("_kernel", StringComparison.Ordinal));
        var instructions = ReadInstructions(kernel).ToArray();

        Assert.Contains(instructions, instruction =>
            instruction.OpCode == OpCodes.Ldc_R8
            && instruction.Operand is double value
            && value == 1000000007d);
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "ConvertToNumber" });
        Assert.DoesNotContain(instructions, instruction =>
            instruction.OpCode == OpCodes.Ldfld
            && instruction.Operand is FieldInfo { FieldType: { } fieldType } field
            && fieldType == typeof(object)
            && field.Name.EndsWith("__MODULUS", StringComparison.Ordinal));
    }

    [Fact]
    public void MutableTopLevelNumber_RetainsLiveBoxedFieldLoad()
    {
        const string source = """
            let MODULUS: number = 7;

            export function kernel(value: number): number {
                return value % MODULUS;
            }
            """;
        Assembly assembly = CompileKernelModule(source);
        MethodInfo kernel = assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.EndsWith("_kernel", StringComparison.Ordinal));
        var instructions = ReadInstructions(kernel).ToArray();

        Assert.Contains(instructions, instruction =>
            instruction.OpCode == OpCodes.Ldfld
            && instruction.Operand is FieldInfo { FieldType: { } fieldType } field
            && fieldType == typeof(object)
            && field.Name.EndsWith("__MODULUS", StringComparison.Ordinal));
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "ConvertToNumber" });
    }

    private static Assembly CompileKernelModule(string kernelSource = KernelSource)
    {
        string virtualBase = Path.Combine(
            Path.GetTempPath(), $"sharpts_numeric_const_{Guid.NewGuid():N}");
        string kernelPath = Path.GetFullPath(Path.Combine(virtualBase, "kernel.ts"));
        string mainPath = Path.GetFullPath(Path.Combine(virtualBase, "main.ts"));
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [kernelPath] = kernelSource,
            [mainPath] = "import { kernel } from './kernel'; console.log(kernel(10));"
        };

        var resolver = new ModuleResolver(mainPath, files);
        var entryModule = resolver.LoadModule(mainPath);
        var modules = resolver.GetModulesInOrder(entryModule);
        var checker = new TypeChecker();
        TypeMap typeMap = TestHarness.CheckModulesOrThrow(checker, modules, resolver);
        var statements = modules.SelectMany(module => module.Statements).ToList();
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"numeric_const_{Guid.NewGuid():N}");
        compiler.CompileModules(modules, resolver, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }

    private static IEnumerable<(OpCode OpCode, object? Operand)> ReadInstructions(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException($"Method '{method.Name}' has no IL body.");
        Module module = method.Module;

        for (int offset = 0; offset < il.Length;)
        {
            byte first = il[offset++];
            short value = first == 0xfe
                ? unchecked((short)(0xfe00 | il[offset++]))
                : first;
            OpCode opCode = OpCodeByValue[value];
            object? operand = opCode.OperandType switch
            {
                OperandType.InlineMethod => module.ResolveMethod(BitConverter.ToInt32(il, offset)),
                OperandType.InlineField => module.ResolveField(BitConverter.ToInt32(il, offset)),
                OperandType.InlineType => module.ResolveType(BitConverter.ToInt32(il, offset)),
                OperandType.InlineR => BitConverter.ToDouble(il, offset),
                OperandType.ShortInlineR => BitConverter.ToSingle(il, offset),
                _ => null
            };

            int operandSize = opCode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or
                    OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI or OperandType.InlineBrTarget or OperandType.InlineField or
                    OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString or
                    OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => throw new InvalidOperationException(
                    $"Unsupported IL operand type {opCode.OperandType}.")
            };

            offset += operandSize;
            yield return (opCode, operand);
        }
    }

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodeByValue =
        typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);
}
