using SharpTS.Diagnostics;
using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class PropertyTypeofGuardDiagnosticTests
{
    private const string Source = """
            function wrongThen(holder: { value: string | number }) { if (typeof holder.value === "string") { const wrong: number = holder.value; } }
            function wrongElse(holder: { value: string | number }) { if (typeof holder.value === "string") {} else { const wrong: string = holder.value; } }
            function ownWrite(holder: { value: string | number }) { if (typeof holder.value === "string") { holder.value = 7; const wrong: string = holder.value; } }
            function receiverWrite(holder: { value: string | number }) { if (typeof holder.value === "string") { holder = { value: 7 }; const wrong: string = holder.value; } }
            function otherReceiver(holder: { value: string | number }, other: { value: string | number }) { if (typeof holder.value === "string") { const wrong: string = other.value; } }
            """ + "\n";

    [Theory]
    [InlineData("Recovery")]
    [InlineData("Modules")]
    public void BranchTypesAndReceiverWritesKeepPreciseDiagnostics(string collection)
    {
        IReadOnlyList<Diagnostic> diagnostics;
        if (collection == "Recovery")
        {
            diagnostics = new TypeChecker(TypeCheckerOptions.Strict)
                .CheckWithRecovery(TestHarness.ParseOrThrow(Source)).Diagnostics;
        }
        else
        {
            Assert.Equal("Modules", collection);
            var path = Path.Combine(Path.GetTempPath(), "property-typeof-guard-" + Guid.NewGuid().ToString("N"), "main.ts");
            var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = Source });
            var entry = resolver.LoadModule(path);
            var checker = new TypeChecker(TypeCheckerOptions.Strict);
            checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
            diagnostics = checker.GetDiagnostics();
        }

        Assert.Equal("TS2322:1,TS2322:2,TS2322:3,TS2322:4,TS2322:5".Split(',').OrderBy(value => value, StringComparer.Ordinal),
            diagnostics.Select(item => (item.TsCode ?? "") + ":" + item.Line)
                .OrderBy(value => value, StringComparer.Ordinal));
    }
}
