using SharpTS.Testing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class GenericLocalParentInstantiationTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["prototype-read-before-local-generic-child", """
            class GenericBase<T> {
                Upper: T;
                constructor(value: T) { this.Upper = value; }
            }
            void GenericBase.prototype;
            function makeGeneric(value: number): any {
                class Child extends GenericBase<number> { constructor() { super(value); } }
                return Child;
            }
            const GenericFirst: any = makeGeneric(113), GenericSecond: any = makeGeneric(127);
            console.log(new GenericFirst().Upper, new GenericSecond().Upper, new GenericFirst().Upper);
            """.ReplaceLineEndings("\r\n"), "113 127 113\n"];
        yield return ["local-generic-child-without-prototype-read", """
            class GenericBase<T> {
                Upper: T;
                constructor(value: T) { this.Upper = value; }
            }
            function makeGeneric(value: number): any {
                class Child extends GenericBase<number> { constructor() { super(value); } }
                return Child;
            }
            const GenericFirst: any = makeGeneric(113), GenericSecond: any = makeGeneric(127);
            console.log(new GenericFirst().Upper, new GenericSecond().Upper, new GenericFirst().Upper);
            """.ReplaceLineEndings("\r\n"), "113 127 113\n"];
        yield return ["closed-2142-generic-expression-parent-control", """
            class Base<T> {
                constructor(value: T) { console.log(value); }
            }
            const D = class extends Base<number> {};
            new D(43);
            """.ReplaceLineEndings("\n"), "43\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void LocalGenericParentsUseConstructedTypes(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        string actual;
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            actual = output;
        }
        else
        {
            actual = TestHarness.Run(source, ExecutionMode.Interpreted);
        }
        Assert.Equal(expected, actual);
    }
}
