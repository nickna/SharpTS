using Xunit;

namespace SharpTS.Tests.CompilerTests;

public partial class StandaloneDllTests
{
    [Fact]
    public void Isolated_GenericClassMethodsPreserveRuntimeParameterStorage()
    {
        using var directory = IntegrationTests.CliTestHelper.CreateTempDirectory();
        var path = directory.CreateFile("main.ts", """
            class Box<Outer> {
                value: Outer;
                constructor(value: Outer) { this.value = value; }
                read<T extends { value: number }>(other: T): number { return other.value; }
                outer<T>(unused: T): Outer { return this.value; }
                choose<T>(value: T): T;
                choose(value: any, unused?: number): any { return value; }
            }
            const box = new Box<string>("saved");
            const outer: string = box.outer(1);
            const chosen: number = box.choose(12);
            console.log(box.read({ value: 11 }), outer, chosen);
            """);
        AssertStandaloneConstructorIdentity(directory.Path, path,
            directory.GetPath("generic-class-method-types.dll"), "11 saved 12\n");
    }
}
