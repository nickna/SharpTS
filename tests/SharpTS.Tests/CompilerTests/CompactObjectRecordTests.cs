using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

/// <summary>
/// Regression coverage for #1412/#1419: exact small record shapes use slot-backed
/// CLR reference types and stable recursive signatures preserve that type, while
/// dynamic observation and mutation retain ordinary JS object behavior.
/// </summary>
public sealed class CompactObjectRecordTests
{
    private const string TreeSource = """
        type TreeNode = { left: TreeNode | null; right: TreeNode | null };

        function buildTree(depth: number): TreeNode {
            if (depth <= 0) return { left: null, right: null };
            return { left: buildTree(depth - 1), right: buildTree(depth - 1) };
        }

        function itemCheck(node: TreeNode | null): number {
            if (node === null) return 1;
            return 1 + itemCheck(node.left) + itemCheck(node.right);
        }

        function binaryTrees(depth: number): number {
            return itemCheck(buildTree(depth));
        }
        """;

    [Fact]
    public void TypedRecursiveRecord_ReadsAndAllocationsUseExactSlotCarrier()
    {
        Assembly assembly = Compile(TreeSource);
        MethodInfo buildTree = FindFunction(assembly, "buildTree");
        MethodInfo itemCheck = FindFunction(assembly, "itemCheck");

        Type carrier = buildTree.ReturnType;
        Assert.StartsWith("$CompactObjectRecord", carrier.Name, StringComparison.Ordinal);
        Assert.Equal(carrier, Assert.Single(itemCheck.GetParameters()).ParameterType);
        Assert.All(
            carrier.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(field => field.Name.StartsWith("_v", StringComparison.Ordinal)),
            field => Assert.Equal(carrier, field.FieldType));
        Assert.All(
            Assert.Single(carrier.GetConstructors()).GetParameters(),
            parameter => Assert.Equal(carrier, parameter.ParameterType));

        Assert.Contains(ReadMembers(buildTree), member =>
            member.OpCode == OpCodes.Newobj &&
            member.Member?.DeclaringType?.Name.StartsWith(
                "$CompactObjectRecord", StringComparison.Ordinal) == true);
        Assert.Contains(ReadMembers(itemCheck), member =>
            member.OpCode == OpCodes.Ldfld &&
            member.Member?.DeclaringType?.Name.StartsWith(
                "$CompactObjectRecord", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(ReadMembers(itemCheck), member =>
            member.Member?.DeclaringType?.IsGenericType == true &&
            member.Member.DeclaringType.GetGenericTypeDefinition() == typeof(Dictionary<,>));
        Assert.DoesNotContain(ReadMembers(itemCheck), member => member.OpCode == OpCodes.Isinst);
        Assert.DoesNotContain(ReadMembers(itemCheck), member =>
            member.Member?.Name == "GetProperty");

        var binaryTrees = FindFunction(assembly, "binaryTrees")
            .CreateDelegate<Func<double, double>>();
        Assert.Equal(16383, binaryTrees(12));
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(16383, binaryTrees(12));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 250_000, 300_000);
        Assert.Empty(TestHarness.CompileAndVerifyOnly(TreeSource));
    }

    [Fact]
    public void DynamicMutationDeletionAndDescriptorsRetainGeneralObjectPath()
    {
        const string source = """
            type Node = { left: Node | null; right: Node | null };
            const node: Node = { left: null, right: null };
            console.log(node.left, node.right, Object.keys(node).join(","));
            node.left = { left: null, right: null };
            console.log(node.left === null, node.left!.right);
            console.log(delete node.right, node.right);
            Object.defineProperty(node, "left", {
                get: () => "getter",
                configurable: true
            });
            console.log(node.left, Object.keys(node).join(","));
            """;

        Assembly assembly = Compile(source);
        Assert.DoesNotContain(
            assembly.GetType("$Program")!
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .SelectMany(ReadMembers),
            member => member.OpCode == OpCodes.Newobj &&
                member.Member?.DeclaringType?.Name.StartsWith(
                    "$CompactObjectRecord", StringComparison.Ordinal) == true);

        Assert.Equal(
            "null null left,right\nfalse null\ntrue undefined\ngetter left\n",
            TestHarness.RunCompiled(source));
    }

    [Fact]
    public void SameArityDifferentShapesRemainDistinct()
    {
        const string source = """
            const first: { x: number; y: number } = { x: 1, y: 2 };
            const second: { left: string; right: string } = { left: "a", right: "b" };
            console.log(first.x, first.y, second.left, second.right);
            """;

        Assert.Equal("1 2 a b\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void ExportedOrValueUsedFunctionRetainsObjectSignature()
    {
        const string source = """
            type Node = { left: Node | null; right: Node | null };

            export function exportedRead(node: Node | null): number {
                if (node === null) return 1;
                return exportedRead(node.left);
            }

            function aliasedRead(node: Node | null): number {
                if (node === null) return 1;
                return aliasedRead(node.left);
            }
            const alias = aliasedRead;
            console.log(alias({ left: null, right: null }));
            """;

        Assembly assembly = Compile(source);
        Assert.Equal(typeof(object),
            Assert.Single(FindFunction(assembly, "exportedRead").GetParameters()).ParameterType);
        Assert.Equal(typeof(object),
            Assert.Single(FindFunction(assembly, "aliasedRead").GetParameters()).ParameterType);
        Assert.Equal("1\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void InternalWalkerCalledFromObjectBoundaryRetainsObjectSignature()
    {
        const string source = """
            type Node = { left: Node | null; right: Node | null };

            function walk(node: Node | null): number {
                if (node === null) return 1;
                return walk(node.left);
            }

            export function entry(node: Node | null): number {
                return walk(node);
            }
            """;

        Assembly assembly = Compile(source);
        Assert.Equal(typeof(object),
            Assert.Single(FindFunction(assembly, "walk").GetParameters()).ParameterType);
        Assert.Equal(typeof(object),
            Assert.Single(FindFunction(assembly, "entry").GetParameters()).ParameterType);
    }

    [Fact]
    public void MutatedRecursiveRecordRetainsObjectSignatures()
    {
        const string source = """
            type Node = { left: Node | null; right: Node | null };

            function build(): Node {
                return { left: null, right: null };
            }
            function walk(node: Node | null): number {
                if (node === null) return 1;
                return walk(node.left);
            }

            const node = build();
            node.left = build();
            console.log(walk(node));
            """;

        Assembly assembly = Compile(source);
        Assert.Equal(typeof(object), FindFunction(assembly, "build").ReturnType);
        Assert.Equal(typeof(object),
            Assert.Single(FindFunction(assembly, "walk").GetParameters()).ParameterType);
        Assert.Equal("1\n", TestHarness.RunCompiled(source));
    }

    private static Assembly Compile(string source)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        var deadCodeInfo = new DeadCodeAnalyzer(typeMap).Analyze(statements);
        var compiler = new ILCompiler($"issue_1412_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, deadCodeInfo);
        return Assembly.Load(compiler.SaveToBytes());
    }

    private static MethodInfo FindFunction(Assembly assembly, string name) =>
        assembly.GetType("$Program")!
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.EndsWith(name, StringComparison.Ordinal));

    private static IEnumerable<(OpCode OpCode, MemberInfo? Member)> ReadMembers(MethodInfo method)
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
            MemberInfo? member = null;
            if (opCode.OperandType is OperandType.InlineMethod or OperandType.InlineField or
                OperandType.InlineType)
            {
                int token = BitConverter.ToInt32(il, offset);
                member = opCode.OperandType switch
                {
                    OperandType.InlineMethod => module.ResolveMethod(token),
                    OperandType.InlineField => module.ResolveField(token),
                    _ => module.ResolveType(token)
                };
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
            yield return (opCode, member);
        }
    }

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodeByValue =
        typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => opCode.Value);
}
