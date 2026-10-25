using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class ScriptClassAliasConstraintTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScriptPreparationKeepsClassAliasConstraintsInsteadOfAny(bool modules)
    {
        const string source = "class A{value=1;} type U=A; function f<T extends U>(value:T){const bad:number=value;}";
        Assert.Equal("TS2322", Assert.Single(Check(source, modules)).TsCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidClassConstrainedMembersRemainAvailable(bool modules)
    {
        const string source = "class A{value=1;} type U=A; function f<T extends U>(value:T):number{return value.value;} const result:number=f(new A());";
        Assert.Empty(Check(source, modules));
    }

    private static IReadOnlyList<SharpTS.Diagnostics.Diagnostic> Check(string source, bool modules)
    {
        if (!modules) return new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics;
        // A cold script preparation must not borrow a prior check's thread-local alias
        // expansion; otherwise a previously resolved class can mask the missing hoist.
        return Task.Factory.StartNew<IReadOnlyList<SharpTS.Diagnostics.Diagnostic>>(() =>
        {
            var checker = new TypeChecker(TypeCheckerOptions.Strict);
            string path = Path.Combine(Path.GetTempPath(), "script-class-constraint-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source }, new TypeScriptProgramOptions { NoLib = true });
            var entry = resolver.LoadModule(path);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            return checker.GetDiagnostics();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).GetAwaiter().GetResult();
    }
}
