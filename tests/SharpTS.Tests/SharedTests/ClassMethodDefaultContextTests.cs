using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ClassMethodDefaultContextTests
{
    public const string IssueSource = """
        class Owner {
            #secret = 7;
            make(read: any = () => this.#secret) { return read(); }
        }
        console.log(new Owner().make());
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-private-default-arrow", IssueSource, "7\n"];
        yield return ["original-super-default-arrow", """
            class Parent { read() { return 7; } }
            class Owner extends Parent {
                make(read: any = () => super.read()) { return read(); }
            }
            console.log(new Owner().make());
            """, "7\n"];
        yield return ["instance-and-static-default-class-contexts", """
            class Parent {
                read() { return 1; }
                static read() { return 2; }
            }
            class Owner extends Parent {
                #instanceSecret = 7;
                static #staticSecret = 8;
                make(read: any = () => this.#instanceSecret + super.read()) { return read(); }
                static make(read: any = () => this.#staticSecret + super.read()) { return read(); }
            }
            console.log(new Owner().make(), Owner.make());
            """, "8 10\n"];
        yield return ["class-expression-forward-private-fields-and-nested-arrows", """
            class Parent {
                read() { return 2; }
                static read() { return 3; }
            }
            const Owner = class Named extends Parent {
                make(read: any = () => () => this.#instanceSecret + super.read()) { return read()(); }
                static make(read: any = () => () => this.#staticSecret + super.read()) { return read()(); }
                #instanceSecret = 5;
                static #staticSecret = 7;
            };
            console.log(new Owner().make(), Owner.make());
            """, "7 10\n"];
        yield return ["direct-defaults-and-following-callable-contexts", """
            class Parent {
                read() { return 2; }
                static read() { return 3; }
            }
            class Owner extends Parent {
                #instanceSecret = 4;
                static #staticSecret = 5;
                make(value: any = this.#instanceSecret + super.read()) { return value; }
                static make(value: any = this.#staticSecret + super.read()) { return value; }
            }
            class After {
                #secret = 9;
                make(value: any = this.#secret) { return value; }
            }
            function outside(value: number = 11) { return value; }
            console.log(new Owner().make(), Owner.make());
            console.log(new Owner().make(20), Owner.make(30), new After().make(), outside());
            """, "6 8\n20 30 9 11\n"];
        yield return ["public-pattern-default-private-and-super-contexts", """
            class Parent {
                read() { return 1; }
                static read() { return 2; }
            }
            class Owner extends Parent {
                #instanceSecret = 7;
                static #staticSecret = 8;
                make({ read = () => this.#instanceSecret + super.read() }: any = {}) { return read(); }
                static make({ read = () => this.#staticSecret + super.read() }: any = {}) { return read(); }
            }
            console.log(new Owner().make(), Owner.make());
            console.log(new Owner().make({ read: () => 20 }), Owner.make({ read: () => 30 }));
            """, "8 10\n20 30\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void MethodDefaultsKeepTheirLexicalClassContext(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
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
