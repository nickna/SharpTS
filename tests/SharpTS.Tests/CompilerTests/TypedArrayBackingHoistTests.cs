using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>Structural proof and fallback coverage for #1481.</summary>
public sealed class TypedArrayBackingHoistTests
{
    [Fact]
    public void ExactInt32Loop_LoadsBackingBeforeBodyAndSkipsAccessors()
    {
        MethodInfo hot = CompileFunction("""
            function hot(n: number): number {
                const data = new Int32Array(n);
                for (let i: number = 0; i < n; i++) {
                    data[i] = data[i] + 1;
                }
                return n;
            }
            """, "hot");

        var instructions = ReadInstructions(hot).ToArray();
        int getBuffer = FindCall(instructions, "GetBuffer");
        int getOffset = FindCall(instructions, "get_ByteOffset");
        int getLength = FindCall(instructions, "get_Length");
        int read = FindCall(instructions, "ReadUnaligned");
        int write = FindCall(instructions, "WriteUnaligned");

        Assert.True(getBuffer >= 0 && getBuffer < read);
        Assert.True(getOffset >= 0 && getOffset < read);
        Assert.True(getLength >= 0 && getLength < read);
        Assert.True(write > read);
        Assert.DoesNotContain(instructions, IsUnboxedAccessorCall);
    }

    [Fact]
    public void ExactUint8Loop_UsesDirectByteElementOperations()
    {
        MethodInfo hot = CompileFunction("""
            function hot(n: number): number {
                const data = new Uint8Array(n);
                for (let i: number = 0; i < n; i++) {
                    data[i] = data[i] + 1;
                }
                return n;
            }
            """, "hot");

        var instructions = ReadInstructions(hot).ToArray();
        Assert.Contains(instructions, instruction => instruction.OpCode == OpCodes.Ldelem_U1);
        Assert.Contains(instructions, instruction => instruction.OpCode == OpCodes.Stelem_I1);
        Assert.DoesNotContain(instructions, IsUnboxedAccessorCall);
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "ReadUnaligned" or "WriteUnaligned" });
    }

    [Fact]
    public void ExactInt32Stencil_CoalescesNeighborBoundsChecks()
    {
        MethodInfo hot = CompileFunction("""
            function hot(n: number): number {
                const data = new Int32Array(n);
                let sum: number = 0;
                for (let i: number = 1; i < n - 1; i++) {
                    sum = sum + (data[i - 1] - 2 * data[i] + data[i + 1]);
                }
                return sum;
            }
            """, "hot");

        var instructions = ReadInstructions(hot).ToArray();
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "GetArrayDataReference" });
        // Three reads in the guarded integer arm and three in the semantic double fallback.
        Assert.Equal(6, instructions.Count(instruction =>
            instruction.Operand is MethodBase { Name: "ReadUnaligned" }));
        Assert.True(instructions.Count(instruction => instruction.OpCode == OpCodes.Conv_I8) >= 3);
        Assert.Contains(instructions, instruction => instruction.OpCode == OpCodes.Mul);
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "DoubleToInt64Bits" });
        Assert.DoesNotContain(instructions, IsUnboxedAccessorCall);
    }

    [Fact]
    public void ExactInt32Kernel_VersionsFillAndReductionAsIntegerLoops()
    {
        MethodInfo hot = CompileFunction("""
            function hot(n: number): number {
                const data = new Int32Array(n);
                for (let i: number = 0; i < n; i++) {
                    data[i] = i * 3 - (i % 7);
                }
                let sum: number = 0;
                for (let i: number = 1; i < n - 1; i++) {
                    sum = sum + (data[i - 1] - 2 * data[i] + data[i + 1]);
                }
                return sum;
            }
            """, "hot");

        Assert.Equal(7d, (double)hot.Invoke(null, [8d])!);
        Assert.True(hot.GetMethodBody()!.LocalVariables.Count(local =>
            local.LocalType == typeof(long)) >= 5);

        var instructions = ReadInstructions(hot).ToArray();
        Assert.Contains(instructions, instruction => instruction.OpCode == OpCodes.Stind_I4);
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "DoubleToInt64Bits" });
    }

    [Fact]
    public void ExactInt32Stencil_FractionalAccumulatorTakesDoubleFallback()
    {
        MethodInfo hot = CompileFunction("""
            function hot(n: number): number {
                const data = new Int32Array(n);
                for (let i: number = 0; i < n; i++) data[i] = i * 3 - (i % 7);
                let sum: number = 0.5;
                for (let i: number = 1; i < n - 1; i++) {
                    sum = sum + (data[i - 1] - 2 * data[i] + data[i + 1]);
                }
                return sum;
            }
            """, "hot");

        Assert.Equal(7.5d, (double)hot.Invoke(null, [8d])!);
    }

    [Fact]
    public void ExactInt32Stencil_NegativeZeroTakesDoubleFallback()
    {
        MethodInfo hot = CompileFunction("""
            function hot(n: number): number {
                const data = new Int32Array(n);
                let sum: number = -0;
                for (let i: number = 1; i < n - 1; i++) {
                    sum = sum + (data[i - 1] - 2 * data[i] + data[i + 1]);
                }
                return 1 / sum;
            }
            """, "hot");

        Assert.Equal(double.NegativeInfinity, (double)hot.Invoke(null, [2d])!);
    }

    [Fact]
    public void ExactInt32Stencil_SafeIntegerExitRoundsThenFallsBack()
    {
        MethodInfo hot = CompileFunction("""
            function hot(n: number): number {
                const data = new Int32Array(3);
                data[0] = 1;
                data[1] = 0;
                data[2] = 1;
                let sum: number = 9007199254740991;
                for (let i: number = 1; i < n - 1; i++) {
                    sum = sum + (data[i - 1] - 2 * data[i] + data[i + 1]);
                }
                return sum;
            }
            """, "hot");

        Assert.Equal(9_007_199_254_740_992d, (double)hot.Invoke(null, [3d])!);
    }

    [Fact]
    public void ExactInt32Stencil_FractionalBoundTakesGenericLoop()
    {
        MethodInfo hot = CompileFunction("""
            function hot(n: number): number {
                const data = new Int32Array(4);
                data[0] = 1;
                data[1] = 2;
                data[2] = 4;
                data[3] = 8;
                let sum: number = 0;
                for (let i: number = 1; i < n - 1; i++) {
                    sum = sum + (data[i - 1] - 2 * data[i] + data[i + 1]);
                }
                return sum;
            }
            """, "hot");

        Assert.Equal(3d, (double)hot.Invoke(null, [3.5d])!);
    }

    [Fact]
    public void ExactInt32Fill_FractionalBoundTakesGenericLoop()
    {
        MethodInfo hot = CompileFunction("""
            function hot(n: number): number {
                const data = new Int32Array(4);
                for (let i: number = 0; i < n; i++) {
                    data[i] = i * 3 - (i % 7);
                }
                return data[3];
            }
            """, "hot");

        Assert.Equal(6d, (double)hot.Invoke(null, [3.5d])!);
    }

    [Fact]
    public void ExactInt32Stencil_UsesWideIntegerArithmeticBeforeOneDoubleConversion()
    {
        MethodInfo hot = CompileFunction("""
            function hot(n: number): number {
                const data = new Int32Array(n);
                data[0] = -2147483648;
                data[1] = 2147483647;
                data[2] = -2147483648;
                let sum: number = 0;
                for (let i: number = 1; i < 2; i++) {
                    sum = sum + (data[i - 1] - 2 * data[i] + data[i + 1]);
                }
                return sum;
            }
            """, "hot");

        // The 3-point term is -8,589,934,590: wider than Int32 but exactly
        // representable as Int64 and JavaScript Number.
        Assert.Equal(-8_589_934_590d, (double)hot.Invoke(null, [3d])!);

        var instructions = ReadInstructions(hot).ToArray();
        Assert.Contains(instructions, instruction => instruction.OpCode == OpCodes.Conv_I8);
        Assert.Contains(instructions, instruction => instruction.OpCode == OpCodes.Conv_R8);
    }

    [Fact]
    public void ExactInt32Stencil_ShortBackingFaultsOnSpecializedAndFallbackPaths()
    {
        MethodInfo hot = CompileFunction("""
            function hot(length: number, initial: number, bound: number): number {
                const data = new Int32Array(length);
                let sum: number = initial;
                for (let i: number = 1; i < bound - 1; i++) {
                    sum = sum + (data[i - 1] - 2 * data[i] + data[i + 1]);
                }
                return sum;
            }
            """, "hot");

        Assert.Contains(ReadInstructions(hot), instruction =>
            instruction.Operand is MethodBase { Name: "DoubleToInt64Bits" });

        foreach (double length in new[] { 0d, 1d, 2d })
        {
            foreach (double initial in new[] { 0d, 0.5d })
            {
                var exception = Assert.Throws<TargetInvocationException>(() =>
                    hot.Invoke(null, [length, initial, 3d]));
                Assert.True(
                    exception.InnerException is IndexOutOfRangeException,
                    $"length={length}, initial={initial}: {exception.InnerException}");
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ExactInt32Stencil_InvalidNeighborStillFaults(int center)
    {
        MethodInfo hot = CompileFunction($$"""
            function hot(n: number): number {
                const data = new Int32Array(n);
                let sum: number = 0;
                for (let i: number = {{center}}; i < {{center + 1}}; i++) {
                    sum = sum + (data[i - 1] - 2 * data[i] + data[i + 1]);
                }
                return sum;
            }
            """, "hot");

        var exception = Assert.Throws<TargetInvocationException>(() => hot.Invoke(null, [3d]));
        Assert.IsType<IndexOutOfRangeException>(exception.InnerException);
    }

    [Fact]
    public void ExactInt32CounterWrite_KeepsRangeSafeArithmeticNative()
    {
        MethodInfo hot = CompileFunction("""
            function hot(n: number): number {
                const data = new Int32Array(n);
                for (let i: number = 0; i < n; i++) {
                    data[i] = i * 3 - (i % 7);
                }
                return n;
            }
            """, "hot");

        var instructions = ReadInstructions(hot).ToArray();
        Assert.Contains(instructions, instruction => instruction.OpCode == OpCodes.Stind_I4);
        Assert.Contains(instructions, instruction => instruction.OpCode == OpCodes.Rem);
        Assert.DoesNotContain(instructions, IsUnboxedAccessorCall);
    }

    [Fact]
    public void Int32CounterWrite_UnsafeRangeRetainsDoubleNarrowing()
    {
        MethodInfo hot = CompileFunction("""
            function hot(n: number): number {
                const data = new Int32Array(n);
                for (let i: number = 0; i < n; i++) {
                    data[i] = i * 100000;
                }
                return n;
            }
            """, "hot");

        var instructions = ReadInstructions(hot).ToArray();
        Assert.DoesNotContain(instructions, instruction => instruction.OpCode == OpCodes.Stind_I4);
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "WriteUnaligned" });
    }

    [Fact]
    public void Int32CounterWrite_NegativeZeroRetainsDoubleSemantics()
    {
        MethodInfo hot = CompileFunction("""
            function hot(n: number): number {
                const data = new Int32Array(n);
                let result: number = 1;
                for (let i: number = 0; i < n; i++) {
                    result = (data[i] = i * -0);
                }
                return 1 / result;
            }
            """, "hot");

        var instructions = ReadInstructions(hot).ToArray();
        Assert.DoesNotContain(instructions, instruction => instruction.OpCode == OpCodes.Stind_I4);
        Assert.Contains(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "WriteUnaligned" });
        Assert.Equal(double.NegativeInfinity, (double)hot.Invoke(null, [1d])!);
    }

    [Theory]
    [MemberData(nameof(FallbackPrograms))]
    public void UnsafeLifetime_RetainsConcreteAccessorFallback(string source)
    {
        var instructions = ReadInstructions(CompileFunction(source, "hot")).ToArray();

        Assert.Contains(instructions, IsUnboxedAccessorCall);
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "GetBuffer" });
    }

    [Fact]
    public void UnsupportedClampedKind_DoesNotHoistBacking()
    {
        var instructions = ReadInstructions(CompileFunction("""
            function hot(n: number): number {
                const data = new Uint8ClampedArray(n);
                for (let i: number = 0; i < n; i++) data[i] = i;
                return n;
            }
            """, "hot")).ToArray();

        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "GetBuffer" or "ReadUnaligned" or "WriteUnaligned" });
    }

    [Fact]
    public void ExactBackingLoop_PassesIlVerification()
    {
        const string source = """
            function hot(n: number): number {
                const data = new Float64Array(n);
                for (let i: number = 0; i < n; i++) data[i] += i * 0.5;
                return n;
            }
            function stencil(n: number): number {
                const data = new Int32Array(n);
                for (let i: number = 0; i < n; i++) data[i] = i * 3 - (i % 7);
                let sum: number = 0;
                for (let i: number = 1; i < n - 1; i++) {
                    sum = sum + (data[i - 1] - 2 * data[i] + data[i + 1]);
                }
                return sum;
            }
            console.log(hot(8), stencil(8));
            """;

        Assert.Empty(Infrastructure.TestHarness.CompileAndVerifyOnly(source));
    }

    public static TheoryData<string> FallbackPrograms => new()
    {
        """
        function observe(value: Int32Array): void {}
        function hot(n: number): number {
            const data = new Int32Array(n);
            observe(data);
            for (let i: number = 0; i < n; i++) data[i] = i;
            return n;
        }
        """,
        """
        function hot(n: number): number {
            let data = new Int32Array(n);
            for (let i: number = 0; i < n; i++) {
                if (i === 1) data = new Int32Array(n);
                data[i] = i;
            }
            return n;
        }
        """,
        """
        function hot(n: number): number {
            const data = new Int32Array(n);
            const alias = data.subarray(0);
            for (let i: number = 0; i < n; i++) data[i] = i;
            return alias.length;
        }
        """,
        """
        function hot(n: number): number {
            const data = new Int32Array(n);
            const backing = data.buffer;
            for (let i: number = 0; i < n; i++) data[i] = i;
            return backing.byteLength;
        }
        """,
        """
        function hot(n: number): number {
            const data = new Int32Array(n);
            const index: any = 0;
            for (let i: number = 0; i < n; i++) data[index] = i;
            return n;
        }
        """,
        """
        function hot(n: number): number {
            const buffer = new ArrayBuffer(n * 4);
            const data = new Int32Array(buffer);
            for (let i: number = 0; i < n; i++) data[i] = i;
            return n;
        }
        """,
        """
        function hot(n: number): number {
            const data = new Int32Array(n);
            const read = (): number => data[0];
            for (let i: number = 0; i < n; i++) data[i] = i;
            return read();
        }
        """
    };

    private static MethodInfo CompileFunction(string source, string name)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"issue_1481_typed_array_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        Assembly assembly = Assembly.Load(compiler.SaveToBytes());
        return assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.EndsWith(name, StringComparison.Ordinal));
    }

    private static int FindCall(
        (OpCode OpCode, MemberInfo? Operand)[] instructions,
        string methodName) =>
        Array.FindIndex(instructions, instruction =>
            instruction.Operand is MethodBase method && method.Name == methodName);

    private static bool IsUnboxedAccessorCall((OpCode OpCode, MemberInfo? Operand) instruction) =>
        instruction.Operand is MethodBase { Name: "GetUnboxed" or "SetUnboxed" };

    private static IEnumerable<(OpCode OpCode, MemberInfo? Operand)> ReadInstructions(MethodInfo method)
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
            if (opCode.OperandType is OperandType.InlineField or OperandType.InlineMethod
                or OperandType.InlineTok or OperandType.InlineType)
            {
                int token = BitConverter.ToInt32(il, offset);
                operand = module.ResolveMember(token);
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
