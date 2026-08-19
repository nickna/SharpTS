using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Regression coverage for #1386: a stable exported function may call its own emitted method
/// directly, while every binding that can resolve to another function value retains live value
/// dispatch.
/// </summary>
public sealed class StableRecursiveModuleFunctionTests
{
    private const string FibonacciSource = """
        export function fibonacci(n: number): number {
            if (n <= 1) return n;
            return fibonacci(n - 1) + fibonacci(n - 2);
        }
        """;

    [Theory, ModeData]
    public void StableExportedSelfRecursion_IsCorrect(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["algorithms.ts"] = FibonacciSource,
            ["main.ts"] = "import { fibonacci } from './algorithms'; console.log(fibonacci(10));"
        };

        Assert.Equal("55\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void ReassignedSelfBinding_DispatchesThroughTheLiveValue(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["model.ts"] = """
                export function recurse(n: number): number {
                    if (n <= 0) return 1;
                    return recurse(n - 1) + 1;
                }
                export function run(): number {
                    const original = recurse;
                    recurse = (_n: number): number => 100;
                    return original(2);
                }
                """,
            ["main.ts"] = "import { run } from './model'; console.log(run());"
        };

        // The first frame is the saved original; its recursive call observes the replacement.
        Assert.Equal("101\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void ImportedFunctionBinding_ContinuesThroughValueDispatchAfterSourceReassignment(
        ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["source.ts"] = """
                export function target(n: number): number {
                    if (n <= 0) return 1;
                    return target(n - 1) + 1;
                }
                export function replace(): void {
                    target = (_n: number): number => 100;
                }
                """,
            ["main.ts"] = """
                import { target, replace } from './source';
                const original = target;
                replace();
                console.log(original(2), target(2));
                """
        };

        // The imported wrapper remains the same callable in both engines, while recursion inside
        // it observes the source module's replaced binding. Most importantly, the imported call
        // itself never becomes a direct call to the source module's emitted method.
        Assert.Equal("101 101\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void LocallyShadowedFunctionName_CallsTheLocalValue(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["model.ts"] = """
                export function recurse(n: number): number {
                    const recurse = (value: number): number => 40 + value;
                    return recurse(n);
                }
                """,
            ["main.ts"] = "import { recurse } from './model'; console.log(recurse(2));"
        };

        Assert.Equal("42\n", TestHarness.RunModules(files, "main.ts", mode));
    }

    [Theory, ModeData]
    public void DirectEvalThatReassignsSelfBinding_RetainsValueDispatch(ExecutionMode mode)
    {
        var files = new Dictionary<string, string>
        {
            ["model.ts"] = """
                export function recurse(n: number): number {
                    if (n <= 0) return 1;
                    return recurse(n - 1) + 1;
                }
                export function run(): number {
                    const original = recurse;
                    eval("recurse = (_n: number): number => 100");
                    return original(2);
                }
                """,
            ["main.ts"] = "import { run } from './model'; console.log(run());"
        };

        Assert.Equal("101\n", TestHarness.RunModules(
            files, "main.ts", mode, allowTypeErrors: true));
    }

    [Fact]
    public void StableExportedSelfRecursion_EmitsDirectCallsWithoutArgumentArrays()
    {
        Assembly assembly = CompileFibonacciModule();
        MethodInfo fibonacci = assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name.EndsWith("_fibonacci", StringComparison.Ordinal));

        var instructions = ReadInstructions(fibonacci).ToList();
        int selfCalls = instructions.Count(instruction =>
            instruction.OpCode == OpCodes.Call &&
            instruction.Operand is MethodBase called &&
            called.MetadataToken == fibonacci.MetadataToken);

        Assert.Equal(2, selfCalls);
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase called && called.Name == "InvokeMethodValue");
        Assert.DoesNotContain(instructions, instruction =>
            instruction.OpCode == OpCodes.Newarr && instruction.Operand is Type type &&
            type == typeof(object));

        var compiled = fibonacci.CreateDelegate<Func<double, double>>();
        Assert.Equal(6765, compiled(20)); // Warm JIT and static runtime state.
        long before = GC.GetAllocatedBytesForCurrentThread();
        double result = compiled(20);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(6765, result);
        Assert.Equal(0, allocated);
    }

    private static Assembly CompileFibonacciModule()
    {
        string virtualBase = Path.Combine(
            Path.GetTempPath(), $"sharpts_1386_{Guid.NewGuid():N}");
        string algorithmsPath = Path.GetFullPath(Path.Combine(virtualBase, "algorithms.ts"));
        string mainPath = Path.GetFullPath(Path.Combine(virtualBase, "main.ts"));
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [algorithmsPath] = FibonacciSource,
            [mainPath] = "import { fibonacci } from './algorithms'; console.log(fibonacci(10));"
        };

        var resolver = new ModuleResolver(mainPath, files);
        var entryModule = resolver.LoadModule(mainPath);
        var modules = resolver.GetModulesInOrder(entryModule);
        var checker = new TypeChecker();
        var typeMap = TestHarness.CheckModulesOrThrow(checker, modules, resolver);
        var statements = modules.SelectMany(module => module.Statements).ToList();
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"issue_1386_{Guid.NewGuid():N}");
        compiler.CompileModules(modules, resolver, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }

    private static IEnumerable<(OpCode OpCode, MemberInfo? Operand)> ReadInstructions(
        MethodInfo method)
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
            MemberInfo? operand = null;

            if (opCode.OperandType is OperandType.InlineMethod or OperandType.InlineType)
            {
                int token = BitConverter.ToInt32(il, offset);
                operand = opCode.OperandType == OperandType.InlineMethod
                    ? module.ResolveMethod(token)
                    : module.ResolveType(token);
            }

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
