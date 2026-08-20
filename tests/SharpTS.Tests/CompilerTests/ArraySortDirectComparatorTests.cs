using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Structural coverage for #1388: only comparators whose arrow identity is
/// statically proven lower through the direct sort helper. Dynamic comparator
/// values retain the general callable path.
/// </summary>
public sealed class ArraySortDirectComparatorTests
{
    [Fact]
    public void StableAnnotatedArrow_UsesDirectHelper_WhileDynamicComparatorDoesNot()
    {
        Assembly assembly = Compile("""
            function sortStable(values: number[]): number[] {
                values.sort((a: number, b: number): number => a - b);
                return values;
            }

            function sortDynamic(
                values: number[],
                compare: (a: number, b: number) => number
            ): number[] {
                values.sort(compare);
                return values;
            }
            """);

        MethodInfo stable = FindFunction(assembly, "sortStable");
        MethodInfo dynamic = FindFunction(assembly, "sortDynamic");

        Assert.Contains(CalledMethods(stable), method => method.Name == "ArraySortDirect");
        Assert.DoesNotContain(CalledMethods(stable), method => method.Name == "ArraySort");
        Assert.Contains(CalledMethods(dynamic), method => method.Name == "ArraySort");
        Assert.DoesNotContain(CalledMethods(dynamic), method => method.Name == "ArraySortDirect");
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"issue_1388_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }

    private static MethodInfo FindFunction(Assembly assembly, string name)
        => assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.EndsWith(name, StringComparison.Ordinal));

    private static IEnumerable<MethodBase> CalledMethods(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray()
            ?? throw new InvalidOperationException(
                $"Method '{method.Name}' has no IL body.");
        Module module = method.Module;

        for (int offset = 0; offset < il.Length;)
        {
            byte first = il[offset++];
            short value = first == 0xfe
                ? unchecked((short)(0xfe00 | il[offset++]))
                : first;
            OpCode opCode = OpCodeByValue[value];

            if (opCode.OperandType == OperandType.InlineMethod)
            {
                int token = BitConverter.ToInt32(il, offset);
                yield return module.ResolveMethod(token)
                    ?? throw new InvalidOperationException(
                        $"Could not resolve method token {token}.");
            }

            int operandSize = opCode.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or
                    OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI or OperandType.InlineBrTarget or
                    OperandType.InlineField or OperandType.InlineMethod or
                    OperandType.InlineSig or OperandType.InlineString or
                    OperandType.InlineTok or OperandType.InlineType or
                    OperandType.ShortInlineR => 4,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch =>
                    4 + 4 * BitConverter.ToInt32(il, offset),
                _ => throw new InvalidOperationException(
                    $"Unsupported IL operand type {opCode.OperandType}.")
            };

            offset += operandSize;
        }
    }

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodeByValue =
        typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);
}
