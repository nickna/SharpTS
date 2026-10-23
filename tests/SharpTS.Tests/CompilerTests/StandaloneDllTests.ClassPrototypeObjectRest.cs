using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    public static IEnumerable<object[]> ClassPrototypeObjectRestCases()
    {
        yield return ["prototype-string-and-record-control", """
            class Owner {}
            const source: any = Owner.prototype;
            source.value = 1;
            const { ...rest } = source;
            const record: any = { value: 1 };
            const { ...recordRest } = record;
            console.log('source', Object.keys(source).join(','), source.value);
            console.log('prototype rest', Object.keys(rest).join(','), rest.value);
            console.log('record rest', Object.keys(recordRest).join(','), recordRest.value);
            """, "source value 1\nprototype rest value 1\nrecord rest value 1\n"];
        yield return ["fresh-data-properties-preserve-proto-and-symbol", """
            const key = Symbol('retained'), replacement = { marker: 7 }, state = { skipped: 0, retained: 0 };
            class Owner {}
            const source: any = Owner.prototype;
            Object.defineProperty(source, '__proto__', { value: replacement, enumerable: true, configurable: true });
            Object.defineProperty(source, 'skip', { get() { state.skipped++; return 1; }, enumerable: false, configurable: true });
            Object.defineProperty(source, key, { get() { state.retained++; return 11; }, enumerable: true, configurable: true });
            const { skip, ...rest } = source;
            const own = Object.getOwnPropertyDescriptor(rest, key)!;
            console.log(skip, state.skipped, state.retained, Object.hasOwn(rest, 'skip'));
            console.log(Object.hasOwn(rest, '__proto__'), rest.__proto__ === replacement,
                Object.getPrototypeOf(rest) === Object.prototype, rest[key]);
            console.log(own.value, own.get === undefined, own.writable, own.enumerable, own.configurable);
            """, "1 1 1 false\ntrue true true 11\n11 true true true true\n"];
    }

    // #2671 separately owns the interpreter's missing class-prototype copy path.
    [Theory, MemberData(nameof(ClassPrototypeObjectRestCases))]
    public void Isolated_Issue2757ClassPrototypeObjectRest_VerifyBeforeExecution(
        string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var sourcePath = directory.CreateFile("main.ts", source);
        var output = directory.GetPath("class-prototype-object-rest-" + name + ".dll");
        var compile = IntegrationTests.CliTestHelper.RunCli(
            $"--no-tsconfig --noLib --compile \"{sourcePath}\" -o \"{output}\" --standalone --verify",
            directory.Path, TimeSpan.FromSeconds(60));

        Assert.True(compile.ExitCode == 0, compile.StandardOutput + compile.StandardError);
        Assert.Contains("IL verification passed.", compile.StandardOutput);
        Assert.All(GetAssemblyReferences(output), reference => Assert.StartsWith("System.", reference));
        Assert.False(File.Exists(directory.GetPath("SharpTS.dll")));
        Assert.Equal(expected, ExecuteCompiledDllIsolated(output, timeoutMs: 30000,
                verifyStandardError: error => Assert.Empty(error), standardInput: ""));
    }
}
