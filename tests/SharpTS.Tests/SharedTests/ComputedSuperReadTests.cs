using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ComputedSuperReadTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["dynamic-super-index", """
            class First { static value = 7; }
            class Derived extends First {
                static read(key: 'value') { return super[key]; }
            }
            console.log(Derived.read('value'));
            """, "7\n"];
        yield return ["literal-super-index", """
            class First { static value = 7; }
            class Derived extends First {
                static read() { return super['value']; }
            }
            console.log(Derived.read());
            """, "7\n"];
        yield return ["named-super-control", """
            class First { static value = 7; }
            class Derived extends First {
                static read() { return super.value; }
            }
            console.log(Derived.read());
            """, "7\n"];
        yield return ["computed-instance-getter-key-once-receiver", """
            let keyReads = 0;
            let getterReads = 0;
            function key(): "value" {
                keyReads++;
                return "value";
            }
            class First {
                tag: string = "base";
                get value() {
                    getterReads++;
                    return this.tag;
                }
            }
            class Derived extends First {
                tag: string = "derived";
                read() { return super[key()]; }
            }
            const derived = new Derived();
            console.log(derived.read(), keyReads, getterReads);
            """, "derived 1 1\n"];
        yield return ["computed-static-getter-key-once-receiver", """
            let keyReads = 0;
            let getterReads = 0;
            function key(): "value" {
                keyReads++;
                return "value";
            }
            class First {
                static tag: string = "base";
                static get value() {
                    getterReads++;
                    return this.tag;
                }
            }
            class Derived extends First {
                static tag: string = "derived";
                static read() { return super[key()]; }
            }
            console.log(Derived.read(), keyReads, getterReads);
            """, "derived 1 1\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ComputedSuperReadsUseTheLexicalHomeAndActualReceiver(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source));
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            var result = SourceExecutionService.Interpret(source);
            Assert.True(result.Success, string.Join("\n", result.Errors) + result.Output);
            Assert.Empty(result.Errors);
            Assert.Equal(expected, result.Output.Replace("\r\n", "\n"));
        }
    }
}
