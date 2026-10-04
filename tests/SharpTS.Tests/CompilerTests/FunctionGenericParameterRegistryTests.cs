using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class FunctionGenericParameterRegistryTests
{
    private static TypeBuilder Owner() => new PersistedAssemblyBuilder(
        new AssemblyName(Guid.NewGuid().ToString("N")), typeof(object).Assembly)
        .DefineDynamicModule("Main").DefineType("Program");

    private static MethodBuilder Method(TypeBuilder owner, string name) => owner.DefineMethod(
        name, MethodAttributes.Public | MethodAttributes.Static, typeof(object), [typeof(object)]);

    [Fact]
    public void ParametersAreProtectedAndSameNamedMethodsHaveIndependentIdentity()
    {
        var owner = Owner();
        var first = Method(owner, "identity");
        var second = Method(owner, "identity");
        var parameters = first.DefineGenericParameters("T", "U").ToArray();
        var other = second.DefineGenericParameters("T");
        var registry = new FunctionGenericParameterRegistry();
        registry.Declare(first, parameters);
        registry.Declare(second, other);
        var declared = registry.Require(first);
        parameters[0] = other[0];
        Assert.Same(first.GetGenericArguments()[0], declared[0]);
        Assert.Single(registry.Require(second));
        Assert.Throws<NotSupportedException>(() => ((IList<GenericTypeParameterBuilder>)declared).Clear());
        registry.CompleteEmission();
        Assert.Same(declared, registry.Require(first));
        Assert.Throws<InvalidOperationException>(() => registry.Declare(first, declared));
        Assert.Throws<InvalidOperationException>(() => registry.CompleteEmission());
        Assert.Throws<InvalidOperationException>(() => new FunctionGenericParameterRegistry().Require(first));
    }

    [Fact]
    public void InvalidDeclarationsPublishNothingAndNonGenericIsExplicit()
    {
        var owner = Owner();
        var method = Method(owner, "pair");
        var parameters = method.DefineGenericParameters("T", "U");
        var foreign = Method(owner, "other").DefineGenericParameters("T", "U");
        var plain = Method(owner, "plain");
        var registry = new FunctionGenericParameterRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.Require(method));
        Assert.Throws<InvalidOperationException>(() => registry.Declare(method, []));
        Assert.Throws<InvalidOperationException>(() => registry.Declare(method, foreign));
        Assert.Throws<InvalidOperationException>(() => registry.Declare(method, [parameters[1], parameters[0]]));
        Assert.Throws<InvalidOperationException>(() => registry.Declare(method, [parameters[0], null!]));
        Assert.Throws<InvalidOperationException>(() => registry.Require(method));
        registry.Declare(method, parameters);
        Assert.Throws<InvalidOperationException>(() => registry.Declare(method, parameters));
        registry.Declare(plain, []);
        Assert.Empty(registry.Require(plain));
        registry.CompleteEmission();
        Assert.True(registry.IsComplete);
    }

    [Fact]
    public void CompletionDetectsChangedOwnerAndAllowsEmptyCompilation()
    {
        var method = Method(Owner(), "late");
        var registry = new FunctionGenericParameterRegistry();
        registry.Declare(method, []);
        method.DefineGenericParameters("T");
        Assert.Throws<InvalidOperationException>(() => registry.CompleteEmission());
        Assert.False(registry.IsComplete);
        var empty = new FunctionGenericParameterRegistry();
        empty.CompleteEmission();
        Assert.True(empty.IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NamespaceAliasesAndSuspendedConsumersUseMethodIdentity(bool modules)
    {
        const string source = """
            namespace Library {
                export function identity<T>(value: T): T { return value; }
                export function fallback<T>(value: T, label: string = "default"): T {
                    console.log(label); return value;
                }
                export function runFallback(): void {
                    console.log(fallback<string>("value", "explicit"));
                }
            }
            namespace Alias {
                export import identity = Library.identity;
                export function run(): void {
                    console.log(identity<number>(42));
                    console.log(identity("inferred"));
                }
            }
            Alias.run();
            Library.runFallback();
            function plainIdentity<T>(value: T): T { return value; }
            function outer(): void {
                function local<T>(value: T): T { return value; }
                console.log(local<string>("nested"));
            }
            outer();
            async function read(): Promise<void> {
                await Promise.resolve(0);
                console.log(plainIdentity<string>("async"));
            }
            function* values(): Generator<string> { yield plainIdentity<string>("generator"); }
            console.log(values().next().value);
            read();
            """;
        const string expected = "42\ninferred\nexplicit\nvalue\nnested\ngenerator\nasync\n";
        if (modules)
        {
            var files = new Dictionary<string, string> { ["main.ts"] = source };
            Assert.Empty(TestHarness.CompileModulesAndVerifyOnly(files, "main.ts"));
            Assert.Equal(expected, TestHarness.RunModulesCompiled(files, "main.ts"));
        }
        else
        {
            Assert.Equal(expected, TestHarness.RunCompiledStandalone(source));
        }
    }
}
