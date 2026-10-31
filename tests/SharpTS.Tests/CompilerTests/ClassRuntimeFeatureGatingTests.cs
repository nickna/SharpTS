using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ClassRuntimeFeatureGatingTests
{
    [Theory]
    [InlineData("console.log(1);", false)]
    [InlineData("class Item {}", true)]
    [InlineData("const Item = class {};", true)]
    public void ReceiverAndPrototypeHelpersFollowUserClassSelection(string source, bool selected)
    {
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        Assert.Equal(selected, new RuntimeFeatureDetector().Detect(statements, typeMap).UsesUserClasses);
        var compiler = new ILCompiler($"class_gate_{Guid.NewGuid():N}");
        compiler.Compile(statements, typeMap, new DeadCodeAnalyzer(typeMap).Analyze(statements));
        var assembly = Assembly.Load(compiler.SaveToBytes());
        var definition = assembly.GetType("$ClassDefinition", throwOnError: true)!;
        Assert.Equal(selected, assembly.GetType("$ClassCapture") is not null);
        foreach (string name in new[] { "Create", "ReadCapture", "WriteCapture", "FindEnvironment", "ValidateParent", "InitializeReceiver", "GetParent", "ReadArgument", "ReadPrototypeProperty", "ReadSuper" })
            Assert.Equal(selected, definition.GetMethod(name) is not null);

        var runtime = assembly.GetType("$Runtime", throwOnError: true)!;
        foreach (string name in new[] { "GetProperty", "SymbolClosedOwner" })
        {
            var method = runtime.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.Equal(selected, CalledMethods(method).Any(called =>
                called.DeclaringType == typeof(Type) && called.Name == nameof(Type.MakeGenericType)));
        }
    }

    private static IEnumerable<MethodBase> CalledMethods(MethodInfo method)
    {
        var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
        for (int offset = 0; offset < bytes.Length;)
        {
            byte first = bytes[offset++];
            short value = first == 0xfe ? unchecked((short)(0xfe00 | bytes[offset++])) : first;
            var code = OpCodeByValue[value];
            if (code.OperandType == OperandType.InlineMethod)
                yield return method.Module.ResolveMethod(BitConverter.ToInt32(bytes, offset))!;
            offset += code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineI or OperandType.ShortInlineVar or OperandType.ShortInlineBrTarget => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, offset),
                _ => 4,
            };
        }
    }

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodeByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(code => code.Value);
}
