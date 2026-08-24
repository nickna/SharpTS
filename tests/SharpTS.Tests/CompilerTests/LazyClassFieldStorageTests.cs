using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class LazyClassFieldStorageTests
{
    [Fact]
    public void TypedOnlyConstructor_LeavesStoreNullAndReadsDoNotMaterialize()
    {
        Assembly assembly = Compile("""
            class Counter {
                value: number;
                constructor(value: number) { this.value = value; }
            }
            const counter = new Counter(1);
            counter.value;
            """);

        Type counterType = assembly.GetType("Counter")!;
        ConstructorInfo constructor = Assert.Single(counterType.GetConstructors());
        var instructions = ReadInstructions(constructor).ToArray();
        Assert.DoesNotContain(instructions, instruction =>
            instruction.OpCode == OpCodes.Newobj
            && instruction.Operand is ConstructorInfo called
            && called.DeclaringType?.IsGenericType == true
            && called.DeclaringType.GetGenericTypeDefinition() == typeof(Dictionary<,>));
        Assert.DoesNotContain(instructions, instruction =>
            instruction.Operand is MethodBase { Name: "$EnsureFields" });

        object instance = constructor.Invoke([1d]);
        Assert.Null(counterType.GetField(
            "_fields", BindingFlags.Instance | BindingFlags.NonPublic));
        FieldInfo fields = counterType.GetField(
            "_fields", BindingFlags.Static | BindingFlags.NonPublic)!;
        var stores = Assert.IsType<ConditionalWeakTable<object, Dictionary<string, object>>>(
            fields.GetValue(null));
        Assert.False(stores.TryGetValue(instance, out _));

        _ = counterType.GetMethod("GetProperty")!.Invoke(instance, ["missing"]);
        Assert.False((bool)counterType.GetMethod("HasProperty")!.Invoke(
            instance, ["missing"])!);
        Assert.False(stores.TryGetValue(instance, out _));
    }

    [Fact]
    public void FirstDynamicWrite_CreatesOneRetainedStore()
    {
        Assembly assembly = Compile("""
            class Counter {
                value: number = 1;
            }
            const counter = new Counter();
            counter.value;
            """);

        Type counterType = assembly.GetType("Counter")!;
        object instance = Assert.Single(counterType.GetConstructors()).Invoke([]);
        FieldInfo fields = counterType.GetField(
            "_fields", BindingFlags.Static | BindingFlags.NonPublic)!;
        var stores = Assert.IsType<ConditionalWeakTable<object, Dictionary<string, object>>>(
            fields.GetValue(null));
        MethodInfo setProperty = counterType.GetMethod("SetProperty")!;

        Assert.False(stores.TryGetValue(instance, out _));
        setProperty.Invoke(instance, ["extra", 2d]);
        Assert.True(stores.TryGetValue(instance, out var first));
        Assert.Equal(2d, first["extra"]);

        setProperty.Invoke(instance, ["second", 3d]);
        Assert.True(stores.TryGetValue(instance, out var second));
        Assert.Same(first, second);
        Assert.Equal(3d, first["second"]);
    }

    [Fact]
    public void DynamicOrEscapingUse_RetainsDirectInstanceStore()
    {
        Assembly assembly = Compile("""
            class DynamicCounter {
                value: number;
                constructor(value: number) { this.value = value; }
            }
            function create(value: number): any {
                const counter: any = new DynamicCounter(value);
                counter.extra = value;
                return counter;
            }
            """);

        Type counterType = assembly.GetType("DynamicCounter")!;
        FieldInfo fields = counterType.GetField(
            "_fields", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(typeof(Dictionary<string, object>), fields.FieldType);
        Assert.Null(counterType.GetField(
            "_fields", BindingFlags.Static | BindingFlags.NonPublic));

        object instance = Assert.Single(counterType.GetConstructors()).Invoke([1d]);
        counterType.GetMethod("SetProperty")!.Invoke(instance, ["extra", 2d]);
        var store = Assert.IsType<Dictionary<string, object>>(fields.GetValue(instance));
        Assert.Equal(2d, store["extra"]);
    }

    [Fact]
    public void CapturedExactLocal_RetainsDirectInstanceStore()
    {
        Assembly assembly = Compile("""
            class CapturedCounter {
                value: number;
                constructor(value: number) { this.value = value; }
            }
            function readLater(): number {
                const counter = new CapturedCounter(1);
                const read = (): number => counter.value;
                return read();
            }
            """);

        Type counterType = assembly.GetType("CapturedCounter")!;
        FieldInfo fields = counterType.GetField(
            "_fields", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(typeof(Dictionary<string, object>), fields.FieldType);
    }

    [Fact]
    public void ClassUsedAsValue_RetainsDirectInstanceStore()
    {
        Assembly assembly = Compile("""
            class AliasedCounter {
                value: number = 1;
            }
            const Constructor: any = AliasedCounter;
            const counter: any = new Constructor();
            counter.extra = 2;
            """);

        Type counterType = assembly.GetType("AliasedCounter")!;
        FieldInfo fields = counterType.GetField(
            "_fields", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(typeof(Dictionary<string, object>), fields.FieldType);
    }

    [Fact]
    public void DynamicSemantics_WorkForDeclarationsExpressionsAndInheritance()
    {
        const string source = """
            class Base {
                constructor() { (this as any).fromBase = 7; }
            }
            class Model extends Base {
                value: number = 1;
            }

            const model: any = new Model();
            console.log(model.fromBase, model.value, model.missing === undefined);
            model.extra = 2;
            console.log(model.extra, Object.keys(model).sort().join(","));
            const roundTrip: any = JSON.parse(JSON.stringify(model));
            console.log(roundTrip.fromBase, roundTrip.value, roundTrip.extra);

            Object.defineProperty(model, "hidden", {
                value: 3, enumerable: false, configurable: true, writable: true
            });
            console.log(model.hidden, Object.keys(model).includes("hidden"));
            Object.freeze(model);
            model.blocked = 4;
            console.log(Object.isFrozen(model), model.blocked === undefined);

            const Expression: any = class { value: number = 5; };
            const expression: any = new Expression();
            console.log(expression.missing === undefined);
            expression.extra = 6;
            console.log(expression.value, expression.extra,
                Object.keys(expression).sort().join(","));
            """;

        Assert.Equal(
            "7 1 true\n" +
            "2 extra,fromBase,value\n" +
            "7 1 2\n" +
            "3 false\n" +
            "true true\n" +
            "true\n" +
            "5 6 extra,value\n",
            TestHarness.RunCompiled(source));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    [Fact]
    public void GenericDeclareAndComputedInitializers_MaterializeAndVerify()
    {
        const string source = """
            const key: string = "computed";
            class Box<T> {
                value: T;
                declare absent: any;
                [key]: number = 2;
                constructor(value: T) { this.value = value; }
            }
            const box: any = new Box<number>(3);
            console.log(box.value, box.absent, box.computed,
                Object.hasOwn(box, "absent"), Object.hasOwn(box, "computed"));
            """;

        Assert.Equal("3 null 2 true true\n", TestHarness.RunCompiled(source));
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source));
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"issue_1459_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }

    private static IEnumerable<(OpCode OpCode, MemberInfo? Operand)> ReadInstructions(
        MethodBase method)
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
