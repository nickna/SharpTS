using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class StablePrimitiveClassMethodTests
{
    [Fact]
    public void ExactConstReceiver_UsesTypedCoreWhilePublicMethodKeepsObjectAbi()
    {
        Assembly assembly = Compile("""
            class Counter {
                value: number;
                constructor(value: number) { this.value = value; }
                step(): number {
                    this.value = this.value + 1;
                    return this.value;
                }
            }
            function exact(n: number): number {
                const counter = new Counter(0);
                let sum: number = 0;
                for (let i: number = 0; i < n; i++) sum = sum + counter.step();
                return sum;
            }
            function uncertain(counter: Counter): number {
                return counter.step();
            }
            """);

        Type counterType = assembly.GetType("Counter")!;
        MethodInfo core = Assert.Single(counterType.GetMethods(
            BindingFlags.Instance | BindingFlags.NonPublic),
            method => method.Name.StartsWith("$typed$step$", StringComparison.Ordinal));
        MethodInfo wrapper = counterType.GetMethod("step")!;
        Assert.Equal(typeof(double), core.ReturnType);
        Assert.Equal(typeof(object), wrapper.ReturnType);

        var exact = ReadInstructions(FindFunction(assembly, "exact")).ToArray();
        Assert.Contains(exact, instruction =>
            instruction.Operand is MethodBase method && method.Name == core.Name);
        Assert.DoesNotContain(exact, instruction =>
            instruction.OpCode == OpCodes.Box && instruction.Operand == typeof(double));

        var uncertain = ReadInstructions(FindFunction(assembly, "uncertain")).ToArray();
        Assert.Contains(uncertain, instruction =>
            instruction.Operand is MethodInfo { Name: "step" } method
            && method.ReturnType == typeof(object));

        var wrapperIl = ReadInstructions(wrapper).ToArray();
        Assert.Contains(wrapperIl, instruction =>
            instruction.Operand is MethodBase method && method.Name == core.Name);
        Assert.Contains(wrapperIl, instruction =>
            instruction.OpCode == OpCodes.Box && instruction.Operand == typeof(double));
    }

    [Fact]
    public void ImmediateNewAndBooleanResult_UseTypedCores()
    {
        Assembly assembly = Compile("""
            class Probe {
                positive(value: number): boolean { return value > 0; }
                value(): number { return 7; }
            }
            function numberResult(): number { return new Probe().value(); }
            function booleanResult(): boolean {
                const probe = new Probe();
                return probe.positive(1);
            }
            """);

        Assert.Contains(ReadInstructions(FindFunction(assembly, "numberResult")), instruction =>
            instruction.Operand is MethodInfo method
            && method.Name.StartsWith("$typed$value$", StringComparison.Ordinal)
            && method.ReturnType == typeof(double));
        Assert.Contains(ReadInstructions(FindFunction(assembly, "booleanResult")), instruction =>
            instruction.Operand is MethodInfo method
            && method.Name.StartsWith("$typed$positive$", StringComparison.Ordinal)
            && method.ReturnType == typeof(bool));
    }

    [Fact]
    public void MutableAndArgumentsSensitiveMethods_RetainPublicWrapperPath()
    {
        Assembly assembly = Compile("""
            class Counter {
                step(): number { return 1; }
                withDefault(value: number = 1): number { return value; }
                countArgs(): number { return arguments.length; }
            }
            function mutable(): number {
                let counter = new Counter();
                return counter.step();
            }
            """);

        Type counterType = assembly.GetType("Counter")!;
        Assert.DoesNotContain(counterType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic),
            method => method.Name.Contains("withDefault", StringComparison.Ordinal)
                || method.Name.Contains("countArgs", StringComparison.Ordinal));
        Assert.Contains(ReadInstructions(FindFunction(assembly, "mutable")), instruction =>
            instruction.Operand is MethodInfo { Name: "step" } method
            && method.ReturnType == typeof(object));
    }

    [Fact]
    public void CatchShadowAndSyntheticNameCollision_StayConservative()
    {
        Assembly assembly = Compile("""
            class Counter {
                step(): number { return 1; }
                $typed$step$0(): number { return 99; }
            }
            function shadowed(): number {
                const counter = new Counter();
                try { throw counter; }
                catch (counter: unknown) { }
                return counter.step();
            }
            function exact(): number {
                const counter = new Counter();
                return counter.step();
            }
            """);

        Type counterType = assembly.GetType("Counter")!;
        MethodInfo core = Assert.Single(counterType.GetMethods(
            BindingFlags.Instance | BindingFlags.NonPublic),
            method => method.Name.StartsWith("$typed$step$", StringComparison.Ordinal));
        Assert.NotEqual("$typed$step$0", core.Name);

        Assert.Contains(ReadInstructions(FindFunction(assembly, "shadowed")), instruction =>
            instruction.Operand is MethodInfo { Name: "step", ReturnType: not null } method
            && method.ReturnType == typeof(object));
        Assert.Contains(ReadInstructions(FindFunction(assembly, "exact")), instruction =>
            instruction.Operand is MethodBase method && method.Name == core.Name);
    }

    [Fact]
    public void EscapedCapturedAndPrototypeObservableReceivers_RetainWrapperPath()
    {
        Assembly escapedAssembly = Compile("""
            class Counter { step(): number { return 1; } }
            function escape(value: Counter): void { }
            function escaped(): number {
                const counter = new Counter();
                escape(counter);
                return counter.step();
            }
            function captured(): number {
                const counter = new Counter();
                const read = (): number => counter.step();
                return counter.step();
            }
            """);

        Assert.Contains(ReadInstructions(FindFunction(escapedAssembly, "escaped")), instruction =>
            instruction.Operand is MethodInfo { Name: "step" } method
            && method.ReturnType == typeof(object));
        Assert.Contains(ReadInstructions(FindFunction(escapedAssembly, "captured")), instruction =>
            instruction.Operand is MethodInfo { Name: "step" } method
            && method.ReturnType == typeof(object));

        Assembly prototypeAssembly = Compile("""
            class Counter { step(): number { return 1; } }
            const observed = Counter.prototype;
            function exact(): number {
                const counter = new Counter();
                return counter.step();
            }
            """);
        Assert.Contains(ReadInstructions(FindFunction(prototypeAssembly, "exact")), instruction =>
            instruction.Operand is MethodInfo { Name: "step" } method
            && method.ReturnType == typeof(object));
    }

    [Fact]
    public void DeclaredPrimitiveField_PreservesAbsentValueThroughObjectWrapper()
    {
        const string source = """
            class Model {
                declare private value: number;
                getValue(): number { return this.value; }
            }
            const model = new Model();
            console.log(model.getValue());
            """;

        Assert.Equal("null\n", TestHarness.RunCompiled(source));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));

        Assembly assembly = Compile(source);
        Type modelType = assembly.GetType("Model")!;
        Assert.DoesNotContain(modelType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic),
            method => method.Name.StartsWith("$typed$getValue$", StringComparison.Ordinal));
        Assert.Equal(typeof(object), modelType.GetMethod("getValue")!.ReturnType);
    }

    [Fact]
    public void VirtualOverridesAndExtractedMethods_PreserveBehavior()
    {
        const string source = """
            class Base {
                step(): number { return 1; }
            }
            class Derived extends Base {
                step(): number { return 2; }
            }
            function invoke(value: Base): number { return value.step(); }
            const derived = new Derived();
            const extracted = derived.step.bind(derived);
            console.log(invoke(derived), derived.step(), extracted());
            """;

        Assert.Equal("2 2 2\n", TestHarness.RunCompiled(source));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    [Fact]
    public void NumberAndBooleanTypedCompanions_PassIlVerification()
    {
        const string source = """
            class Values {
                next(value: number): number { return value + 1; }
                ready(value: number): boolean { return value > 0; }
            }
            function run(): number {
                const values = new Values();
                return values.ready(1) ? values.next(2) : 0;
            }
            console.log(run());
            """;

        Assert.Equal("3\n", TestHarness.RunCompiled(source));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"issue_1457_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }

    private static MethodInfo FindFunction(Assembly assembly, string name) =>
        assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.EndsWith(name, StringComparison.Ordinal));

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
