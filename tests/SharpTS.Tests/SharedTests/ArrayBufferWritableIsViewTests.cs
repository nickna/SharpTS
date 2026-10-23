using SharpTS.Modules;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ArrayBufferWritableIsViewTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["public", """
            const target: any = ArrayBuffer;
            console.log(Object.getOwnPropertyDescriptor(target, "isView")!.writable);
            try {
                target.isView = 17;
                console.log(target.isView);
            } catch (error) { console.log("write threw"); }
            """.ReplaceLineEndings("\n") + "\n", "true\n17\n"];
        yield return ["assignment-observability", """
            const target: any = ArrayBuffer;
            const held: any = target.isView;
            const state = { rhs: 0 };
            function replacement() { state.rhs++; return 17; }
            try {
                console.log((target['isView'] = replacement()), state.rhs);
                const descriptor: any = Object.getOwnPropertyDescriptor(target, 'isView');
                console.log(target.isView, Reflect.get(target, 'isView'), descriptor.value,
                    descriptor.writable, descriptor.enumerable, descriptor.configurable);
                console.log(ArrayBuffer.isView === target.isView, (ArrayBuffer.prototype as any).constructor === target);
                target.isView = null;
                console.log(target.isView === null, Object.getOwnPropertyDescriptor(target, 'isView')!.value === null);
                target.isView = undefined;
                console.log(target.isView === undefined, Object.hasOwn(target, 'isView'));
                target.isView = held;
                console.log(target.isView === held, target.isView(new Uint8Array(1)), state.rhs);
            } catch (error) { console.log('write threw'); }
            """.ReplaceLineEndings("\n") + "\n", "17 1\n17 17 17 true false true\ntrue true\ntrue true\ntrue true\ntrue true 1\n"];
    }

    public static string FreshRealmSource => """
        console.log(typeof ArrayBuffer.isView, ArrayBuffer.isView(new Uint8Array(1)));
        """.ReplaceLineEndings("\n") + "\n";

    public const string FreshRealmExpected = "function true\n";

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void WritableIsViewAssignmentsPreserveDataDescriptorObservability(
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

    [Fact]
    public void InterpretedRunsKeepArrayBufferIsViewInFreshRealms()
    {
        var publicCase = Cases().Single(row => (string)row[0] == "public");
        var publicSource = (string)publicCase[1];
        AssertStrictSource(publicSource);
        Assert.Equal((string)publicCase[2], TestHarness.RunInterpreted(publicSource));
        var freshSource = FreshRealmSource;
        AssertStrictSource(freshSource);
        Assert.Equal(FreshRealmExpected, TestHarness.RunInterpreted(freshSource));
    }

    internal static void AssertStrictSource(string source)
    {
        var path = Path.Combine(Path.GetTempPath(), "array-buffer-writable-isview-" + Guid.NewGuid().ToString("N"), "main.ts");
        var resolver = new ModuleResolver(path, new Dictionary<string, string> { [path] = source });
        var entry = resolver.LoadModule(path);
        var checker = new TypeChecker(TypeCheckerOptions.Strict);
        checker.CheckModules(resolver.GetModulesInOrder(entry), resolver);
        Assert.Empty(checker.GetDiagnostics());
    }
}
