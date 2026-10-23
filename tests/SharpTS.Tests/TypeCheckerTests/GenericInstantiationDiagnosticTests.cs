using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class GenericInstantiationDiagnosticTests
{
    public static readonly string PublicSource = """
        function identity<T>(value: T): T { return value; }
        const numberIdentity = identity<number>;
        """.ReplaceLineEndings("\n") + "\n";

    private static readonly string NegativeSource = """
        function identity<T>(value: T): T { return value; }
        const numberIdentity = identity<number>;
        const value: string = numberIdentity(1);
        numberIdentity("wrong");
        """.ReplaceLineEndings("\n") + "\n";

    [Fact]
    public void PublicSyntaxRetainsInstantiationWithoutInvocation()
    {
        var statements = TestHarness.ParseOrThrow(PublicSource);
        var initializer = statements.OfType<Stmt.Const>().Single().Initializer;
        Assert.NotNull(initializer);
        Assert.IsNotType<Expr.Call>(initializer);
        // Reflection keeps this regression buildable against the pre-feature baseline.
        Assert.Equal("Instantiation", initializer.GetType().Name);
        var operandProperty = initializer.GetType().GetProperty("Expression");
        var argumentsProperty = initializer.GetType().GetProperty("TypeArgs");
        Assert.NotNull(operandProperty);
        Assert.NotNull(argumentsProperty);
        var operand = Assert.IsType<Expr.Variable>(operandProperty.GetValue(initializer));
        Assert.Equal("identity", operand.Name.Lexeme);
        Assert.Equal(new[] { "number" },
            Assert.IsAssignableFrom<IEnumerable<string>>(argumentsProperty.GetValue(initializer)));
    }

    [Fact]
    public void PublicInstantiationIsAcceptedInStrictModules() =>
        Assert.Empty(CollectStrictModules(PublicSource));

    [Fact]
    public void SpecializedCallableRejectsWrongArgumentAndReturnType()
    {
        var diagnostics = CollectStrictModules(NegativeSource);
        Assert.Equal(new[] { (Code: "TS2322", Line: 3), (Code: "TS2345", Line: 4) },
            diagnostics.OrderBy(item => item.Line)
                .Select(item => (Code: item.TsCode ?? "", item.Line)));
    }

    internal static IReadOnlyList<Diagnostic> CollectStrictModules(string source)
    {
        var path = Path.Combine(Path.GetTempPath(), "generic-instantiation-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        return checker.GetDiagnostics();
    }
}
