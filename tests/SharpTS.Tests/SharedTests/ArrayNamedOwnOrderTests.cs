using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ArrayNamedOwnOrderTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public", """
            let reads = 0;
            const target: any = [];
            Object.defineProperty(target, 'first', { get() { reads++; return 1; }, enumerable: true, configurable: true });
            target.second = 2;
            console.log(Object.keys(target).join(','), Object.getOwnPropertyNames(target).filter(key => key === 'first' || key === 'second').join(','), reads);
            """.ReplaceLineEndings("\n") + "\n", "first,second first,second 0\n"];
        yield return ["lifecycle", """
            let reads = 0;
            const target: any[] = [];
            const named: any = target;
            named.early = 1;
            Object.defineProperty(target, 'first', { get() { reads++; return 2; }, enumerable: true, configurable: true });
            named.last = 3;
            Object.defineProperty(target, 'hidden', { value: 4, enumerable: false, configurable: true });
            console.log(Object.keys(target).join(','), Object.getOwnPropertyNames(target).join(','), reads);
            Object.defineProperty(target, 'first', { value: 2, writable: true, enumerable: true, configurable: true });
            Object.defineProperty(target, 'early', { get() { reads++; return 1; }, enumerable: true, configurable: true });
            console.log(Object.keys(target).join(','), Object.getOwnPropertyNames(target).join(','), reads);
            delete named.first;
            Object.defineProperty(target, 'first', { get() { reads++; return 2; }, enumerable: true, configurable: true });
            console.log(Object.keys(target).join(','), Object.getOwnPropertyNames(target).join(','), reads);
            delete named.early;
            named.early = 5;
            console.log(Object.keys(target).join(','), Object.getOwnPropertyNames(target).join(','), reads);
            """.ReplaceLineEndings("\n") + "\n", "early,first,last length,early,first,last,hidden 0\nearly,first,last length,early,first,last,hidden 0\nearly,last,first length,early,last,hidden,first 0\nlast,first,early length,last,hidden,first,early 0\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void NamedKeysPreserveCreationOrderWithoutReadingAccessors(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        AssertStrictSource(source);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            Assert.Equal(expected, TestHarness.RunInterpreted(source));
        }
    }

    internal static void AssertStrictSource(string source)
    {
        var path = Path.Combine(Path.GetTempPath(), "array-named-own-order-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Empty(checker.GetDiagnostics());
    }
}
