using System.Reflection;
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
    }
}
