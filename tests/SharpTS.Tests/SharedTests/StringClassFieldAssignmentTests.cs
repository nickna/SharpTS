using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class StringClassFieldAssignmentTests
{
    public const string IssueSource = """
        class Field {
            constructor(public value: string = "fallback") {}
        }
        console.log(new Field(null as any).value === null, new Field(null as any).value);
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-defaulted-property", IssueSource, "true null\n"];
        yield return ["required-any-control", """
            class Field {
                value: string;
                constructor(value: any) { this.value = value; }
            }
            console.log(new Field(null).value === null, new Field(null).value);
            """, "true null\n"];
        yield return ["defaults-and-raw-parameter", """
            class Field {
                value: string;
                constructor(value: string = "fallback") {
                    this.value = value;
                    console.log(value === null, this.value === null, this.value);
                }
            }
            new Field();
            new Field(undefined);
            new Field(null as any);
            new Field("present");
            """, "false false fallback\nfalse false fallback\ntrue true null\nfalse false present\n"];
        yield return ["typed-null-transitions-and-projections", """
            class Field {
                constructor(public value: string = "fallback") {}
                replace(value: string = "next"): any { return this.value = value; }
            }
            function show(field: Field) {
                const alias: any = field;
                const descriptor: any = Object.getOwnPropertyDescriptor(field, "value");
                const copy: any = Object.assign({}, field);
                console.log(field.value, alias.value, Reflect.get(field, "value"), descriptor.value, copy.value);
            }
            const field = new Field(null as any);
            show(field);
            console.log(field.replace("present"));
            show(field);
            console.log(field.replace(null as any) === null);
            show(field);
            const alias: any = field;
            alias.value = "dynamic";
            show(field);
            alias.value = null;
            show(field);
            """, "null null null null null\npresent\npresent present present present present\ntrue\nnull null null null null\ndynamic dynamic dynamic dynamic dynamic\nnull null null null null\n"];
        yield return ["class-expression", """
            const Field = class {
                constructor(public value: string = "fallback") {}
            };
            console.log(new Field().value, new Field(undefined).value);
            console.log(new Field(null as any).value === null, new Field("present").value);
            """, "fallback fallback\ntrue present\n"];
        yield return ["frozen-writes-return-raw-values", """
            const state = { calls: 0, receivers: 0, hooks: 0 };
            const raw: any = {
                toString() { state.hooks++; throw "converted"; }
            };
            class Field { value: string = "present"; }
            function replace(field: Field, value: string = "next"): any {
                state.calls++;
                return field.value = value;
            }
            const field = new Field();
            function receiver(): Field { state.receivers++; return field; }
            Object.freeze(field);
            console.log(replace(receiver(), null as any) === null, field.value);
            const result: any = replace(receiver(), raw);
            console.log(Object.is(result, raw), field.value, state.calls, state.receivers, state.hooks);
            """, "true present\ntrue present 2 2 0\n"];
        yield return ["optional-and-defaulted-parameter-properties",
            TypeCheckerTests.OptionalParameterPropertyTests.IssueSource,
            "missing missing fallback 0\nmissing missing fallback 0\nnull null null 1\nok ok named 2\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void StringFieldAssignmentsPreserveReferences(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
