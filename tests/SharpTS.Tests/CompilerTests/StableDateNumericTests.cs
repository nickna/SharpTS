using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class StableDateNumericTests
{
    private static readonly string[] NumericGetterHelpers =
    [
        "DateGetTime",
        "DateGetFullYear",
        "DateGetMonth",
        "DateGetDate",
        "DateGetDay",
        "DateGetHours",
        "DateGetMinutes",
        "DateGetSeconds",
        "DateGetMilliseconds",
        "DateGetTimezoneOffset",
        "DateGetUTCFullYear",
        "DateGetUTCMonth",
        "DateGetUTCDate",
        "DateGetUTCDay",
        "DateGetUTCHours",
        "DateGetUTCMinutes",
        "DateGetUTCSeconds",
        "DateGetUTCMilliseconds",
        "DateGetYear",
        "DateValueOf",
    ];

    private static readonly string[] SimpleSetterHelpers =
    [
        "DateSetTime",
        "DateSetDate",
        "DateSetMilliseconds",
        "DateSetUTCDate",
        "DateSetUTCMilliseconds",
        "DateSetYear",
    ];

    [Fact]
    public void NumericGettersAndValueOfStayUnboxed()
    {
        const string source = """
            function readAll(d: Date): number {
                return d.getTime()
                    + d.getFullYear() + d.getMonth() + d.getDate() + d.getDay()
                    + d.getHours() + d.getMinutes() + d.getSeconds() + d.getMilliseconds()
                    + d.getTimezoneOffset()
                    + d.getUTCFullYear() + d.getUTCMonth() + d.getUTCDate() + d.getUTCDay()
                    + d.getUTCHours() + d.getUTCMinutes() + d.getUTCSeconds()
                    + d.getUTCMilliseconds() + d.getYear() + d.valueOf();
            }
            """;

        MethodInfo method = FindFunction(Compile(source), "readAll");
        var instructions = ReadInstructions(method).ToArray();
        string[] calls = DateHelperCalls(instructions).ToArray();

        Assert.Equal(NumericGetterHelpers, calls);
        Assert.DoesNotContain(instructions, IsDoubleBox);
        Assert.DoesNotContain(instructions, IsDoubleUnbox);
    }

    [Fact]
    public void DiscardedSimpleSetterResultsArePoppedWithoutBoxing()
    {
        const string source = """
            function discard(d: Date, value: number): void {
                d.setTime(value);
                d.setDate(value);
                d.setMilliseconds(value);
                d.setUTCDate(value);
                d.setUTCMilliseconds(value);
                d.setYear(value);
            }
            """;

        MethodInfo method = FindFunction(Compile(source), "discard");
        var instructions = ReadInstructions(method).ToArray();
        var setterIndexes = instructions
            .Select((instruction, index) => (instruction, index))
            .Where(entry => entry.instruction.Operand is MethodBase called
                && SimpleSetterHelpers.Contains(called.Name, StringComparer.Ordinal))
            .ToArray();

        Assert.Equal(SimpleSetterHelpers, setterIndexes
            .Select(entry => ((MethodBase)entry.instruction.Operand!).Name));
        Assert.All(setterIndexes, entry =>
            Assert.Equal(OpCodes.Pop, instructions[entry.index + 1].OpCode));
        Assert.DoesNotContain(instructions, IsDoubleBox);
        Assert.DoesNotContain(instructions, IsDoubleUnbox);
    }

    [Fact]
    public void AnyResultContextsBoxAtTheActualBoundary()
    {
        const string source = """
            function getterAny(d: Date): any {
                return d.getTime();
            }
            function setterAny(d: Date): any {
                return d.setTime(1);
            }
            """;

        Assembly assembly = Compile(source);
        AssertCallIsFollowedByDoubleBox(FindFunction(assembly, "getterAny"), "DateGetTime");
        AssertCallIsFollowedByDoubleBox(FindFunction(assembly, "setterAny"), "DateSetTime");
    }

    [Fact]
    public void NumericDateLoopDoesNotAllocatePerIteration()
    {
        const string source = """
            function dateLoop(n: number): number {
                const date = new Date(0);
                let sum: number = 0;
                for (let i: number = 0; i < n; i++) {
                    date.setTime(i);
                    sum = sum + date.getTime();
                }
                return sum;
            }
            """;

        MethodInfo method = FindFunction(Compile(source), "dateLoop");
        var dateLoop = method.CreateDelegate<Func<double, double>>();

        Assert.Equal(45, dateLoop(10));
        _ = dateLoop(100_000);

        long smallBefore = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(499_500, dateLoop(1_000));
        long smallAllocated = GC.GetAllocatedBytesForCurrentThread() - smallBefore;

        long largeBefore = GC.GetAllocatedBytesForCurrentThread();
        double result = dateLoop(100_000);
        long largeAllocated = GC.GetAllocatedBytesForCurrentThread() - largeBefore;

        Assert.Equal(4_999_950_000, result);
        Assert.True(largeAllocated <= smallAllocated + 8_192,
            $"Stable Date numeric allocations scaled with the loop: "
            + $"{smallAllocated:N0} bytes for 1,000 iterations and "
            + $"{largeAllocated:N0} bytes for 100,000 iterations.");
    }

    [Fact]
    public void PrototypeMutationRetainsDynamicDispatch()
    {
        const string source = """
            function read(d: Date): number {
                return d.getTime();
            }
            Date.prototype.getTime = function(): number { return 77; };
            console.log(read(new Date(0)));
            """;

        Assembly assembly = Compile(source);
        var instructions = ReadInstructions(FindFunction(assembly, "read")).ToArray();

        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "InvokeMethodValue" });
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "DateGetTime" });
        Assert.Equal("77\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void OwnOverridesAccessorsAliasesAndAmbiguousReceiversRemainObservable()
    {
        const string source = """
            const own = new Date(0);
            (own as any).getTime = function(): number { return 11; };
            console.log(own.getTime());

            const accessor = new Date(0);
            Object.defineProperty(accessor, "getTime", {
                configurable: true,
                get: function(): any {
                    console.log("lookup");
                    return function(): number { return 22; };
                }
            });
            console.log(accessor.getTime());

            const borrowedDate = new Date(33);
            const borrowed: any = borrowedDate.getTime;
            console.log(borrowed.call(borrowedDate));

            const ambiguous: any = new Date(44);
            ambiguous.getTime = function(): number { return 55; };
            console.log(ambiguous.getTime());
            """;

        Assert.Equal("11\nlookup\n22\n33\n55\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void StableAndFallbackShapesPassIlVerification()
    {
        const string source = """
            function stable(d: Date, n: number): number {
                d.setTime(n);
                return d.getTime() + d.valueOf();
            }
            function ambiguous(d: any): any {
                return d.getTime();
            }
            console.log(stable(new Date(0), 5), ambiguous(new Date(7)));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);

        Assert.Empty(errors);
        Assert.Equal("10 7\n", output);
    }

    private static void AssertCallIsFollowedByDoubleBox(MethodInfo method, string helperName)
    {
        var instructions = ReadInstructions(method).ToArray();
        int callIndex = Array.FindIndex(instructions, instruction =>
            instruction.Operand is MethodBase called && called.Name == helperName);

        Assert.True(callIndex >= 0, $"No call to {helperName} was emitted.");
        Assert.True(IsDoubleBox(instructions[callIndex + 1]),
            $"The {helperName} result was not boxed at the any boundary.");
        Assert.DoesNotContain(instructions, IsDoubleUnbox);
    }

    private static IEnumerable<string> DateHelperCalls(
        IEnumerable<(OpCode OpCode, MemberInfo? Operand)> instructions) =>
        instructions
            .Select(instruction => instruction.Operand)
            .OfType<MethodBase>()
            .Select(method => method.Name)
            .Where(name => name.StartsWith("DateGet", StringComparison.Ordinal)
                || name == "DateValueOf");

    private static bool IsDoubleBox((OpCode OpCode, MemberInfo? Operand) instruction) =>
        instruction.OpCode == OpCodes.Box && instruction.Operand == typeof(double);

    private static bool IsDoubleUnbox((OpCode OpCode, MemberInfo? Operand) instruction) =>
        instruction.OpCode == OpCodes.Unbox_Any && instruction.Operand == typeof(double);

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        TypeMap typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"stable_date_numeric_{Guid.NewGuid():N}");
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
