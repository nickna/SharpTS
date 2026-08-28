using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class StableRegExpTestTests
{
    private const string ValidatorSource = """
        function validate(input: string, n: number): number {
            let valid: number = 0;
            for (let i: number = 0; i < n; i++) {
                if (/^[a-z]+$/.test(input)) valid++;
            }
            return valid;
        }
        """;

    [Fact]
    public void StableLiteralTest_UsesNativeBoolAndDirectTestMethod()
    {
        Assembly assembly = Compile(ValidatorSource);
        MethodInfo validate = FindFunction(assembly, "validate");
        var instructions = ReadInstructions(validate).ToArray();

        Assert.Contains(instructions, instruction =>
            instruction.OpCode == OpCodes.Callvirt
            && instruction.Operand is MethodBase { Name: "Test" } called
            && called.DeclaringType?.Name == "$RegExp");
        Assert.DoesNotContain(instructions, instruction =>
            instruction.OpCode == OpCodes.Box && instruction.Operand == typeof(bool));
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase
            {
                Name: "Exec" or "RegExpTest" or "InvokeMethodValue"
            });
    }

    [Fact]
    public void StableLiteralTest_DoesNotAllocatePerIteration()
    {
        MethodInfo validateMethod = FindFunction(Compile(ValidatorSource), "validate");
        var validate = validateMethod.CreateDelegate<Func<string, double, double>>();

        Assert.Equal(10, validate("abcdefghij", 10));
        _ = validate("abcdefghij", 100_000);

        long smallBefore = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(1_000, validate("abcdefghij", 1_000));
        long smallAllocated = GC.GetAllocatedBytesForCurrentThread() - smallBefore;

        long largeBefore = GC.GetAllocatedBytesForCurrentThread();
        double result = validate("abcdefghij", 100_000);
        long largeAllocated = GC.GetAllocatedBytesForCurrentThread() - largeBefore;

        Assert.Equal(100_000, result);
        Assert.True(largeAllocated <= smallAllocated + 8_192,
            $"Stable RegExp.test allocations scaled with the loop: "
            + $"{smallAllocated:N0} bytes for 1,000 iterations and "
            + $"{largeAllocated:N0} bytes for 100,000 iterations.");
    }

    [Theory, ModeData]
    public void EscapedReceiver_OwnTestAndExecOverridesRemainObservable(ExecutionMode mode)
    {
        const string source = """
            const r: any = /x/;
            r.exec = function(value: any): any {
                console.log("own-exec", value);
                return { matched: true };
            };
            console.log(r.test("abc"));
            r.test = function(value: any): boolean {
                console.log("own-test", value);
                return false;
            };
            console.log(r.test("abc"));
            """;

        Assert.Equal("own-exec abc\ntrue\nown-test abc\nfalse\n",
            TestHarness.Run(source, mode));
    }

    [Fact]
    public void PrototypeTestAndExecOverridesRemainObservable()
    {
        const string source = """
            const originalTest: any = Object.getOwnPropertyDescriptor(RegExp.prototype, "test");
            const originalExec: any = Object.getOwnPropertyDescriptor(RegExp.prototype, "exec");
            try {
                (RegExp.prototype as any).exec = function(value: any): any {
                    console.log("proto-exec", value);
                    return { matched: true };
                };
                console.log(/x/.test("abc"));
                (RegExp.prototype as any).test = function(value: any): boolean {
                    console.log("proto-test", value);
                    return false;
                };
                console.log(/x/.test("abc"));
            } finally {
                Object.defineProperty(RegExp.prototype, "test", originalTest);
                Object.defineProperty(RegExp.prototype, "exec", originalExec);
            }
            """;

        Assert.Equal("proto-exec abc\ntrue\nproto-test abc\nfalse\n",
            TestHarness.RunCompiled(source));
    }

    [Fact]
    public void TestAndExecAccessorsRemainObservable()
    {
        const string source = """
            const r: any = /x/;
            Object.defineProperty(r, "exec", {
                configurable: true,
                get: function(): any {
                    console.log("get-exec");
                    return function(): any { return { matched: true }; };
                }
            });
            console.log(r.test("abc"));

            const originalTest: any = Object.getOwnPropertyDescriptor(RegExp.prototype, "test");
            try {
                Object.defineProperty(RegExp.prototype, "test", {
                    configurable: true,
                    get: function(): any {
                        console.log("get-test");
                        return function(): boolean { return false; };
                    }
                });
                console.log(/x/.test("abc"));
            } finally {
                Object.defineProperty(RegExp.prototype, "test", originalTest);
            }
            """;

        Assert.Equal("get-exec\ntrue\nget-test\nfalse\n", TestHarness.RunCompiled(source));
    }

    [Theory, ModeData]
    public void GlobalStickyAndNonWritableLastIndexRemainObservable(ExecutionMode mode)
    {
        const string source = """
            const globalRx: any = /a/g;
            console.log(globalRx.test("aa"), globalRx.lastIndex);
            console.log(globalRx.test("aa"), globalRx.lastIndex);
            console.log(globalRx.test("aa"), globalRx.lastIndex);

            const stickyRx: any = /a/y;
            stickyRx.lastIndex = 1;
            console.log(stickyRx.test("ba"), stickyRx.lastIndex);

            const locked: any = /z/g;
            Object.defineProperty(locked, "lastIndex", { writable: false });
            try {
                locked.test("x");
            } catch (error) {
                console.log(error instanceof TypeError);
            }
            """;

        Assert.Equal("true 1\ntrue 2\nfalse 0\ntrue 2\ntrue\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AbruptCompletionsRemainObservable(ExecutionMode mode)
    {
        const string source = """
            const r: any = /x/;
            let argumentRuns: number = 0;
            Object.defineProperty(r, "test", {
                get: function(): any {
                    console.log("get-test");
                    throw new Error("lookup");
                }
            });
            try {
                r.test((argumentRuns++, "x"));
            } catch (error) {
                console.log((error as Error).message, argumentRuns);
            }

            const coercion: any = {
                toString: function(): string {
                    console.log("coerce");
                    throw new Error("stringify");
                }
            };
            try {
                /x/.test(coercion);
            } catch (error) {
                console.log((error as Error).message);
            }
            """;

        Assert.Equal("get-test\nlookup 0\ncoerce\nstringify\n",
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void BorrowedTest_ValidatesReceiverBeforeCoercingArgument(ExecutionMode mode)
    {
        const string source = """
            let coercions: number = 0;
            const argument: any = {
                toString: function(): string {
                    coercions++;
                    throw new Error("coerced");
                }
            };

            function check(receiver: any): void {
                try {
                    RegExp.prototype.test.call(receiver, argument);
                } catch (error) {
                    console.log(error instanceof TypeError, coercions);
                }
            }

            check(undefined);
            check(1n);
            """;

        Assert.Equal("true 0\ntrue 0\n", TestHarness.Run(source, mode));
    }

    [Fact]
    public void StableAndFallbackShapesPassIlVerification()
    {
        const string source = ValidatorSource + """
            function escaped(r: RegExp, input: string): boolean {
                return r.test(input);
            }
            function stateful(input: string): boolean {
                return /x/g.test(input) || /x/y.test(input);
            }
            console.log(validate("abc", 2), escaped(/x/, "x"), stateful("x"));
            """;

        var (errors, output) = TestHarness.CompileVerifyAndRun(source);
        Assert.Empty(errors);
        Assert.Equal("2 true true\n", output);
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        TypeMap typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"stable_regexp_test_{Guid.NewGuid():N}");
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
