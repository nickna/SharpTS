using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class UndefinedClassFieldValueTests
{
    public const string IssueSource = """
        class Field {
            value: string | undefined;
            constructor(value: any) { this.value = value; }
        }
        console.log(new Field(undefined).value === undefined);
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["original-string-field", IssueSource, "true\n"];
        yield return ["direct-and-dynamic-writes", """
            function kind(value: any): string {
                return value === undefined ? "missing" : value === null ? "null" : value;
            }
            class Field {
                value: string | undefined;
                constructor(value: any) { this.value = value; }
            }
            const field = new Field(undefined);
            console.log(kind(field.value));
            field.value = "direct";
            console.log(kind(field.value));
            field.value = undefined;
            console.log(kind(field.value));
            const alias: any = field;
            alias.value = "dynamic";
            console.log(kind(field.value));
            alias.value = undefined;
            console.log(kind(field.value));
            alias.value = null;
            console.log(kind(field.value));
            """, "missing\ndirect\nmissing\ndynamic\nmissing\nnull\n"];
        yield return ["alias-and-initializer", """
            type OptionalText = string | undefined;
            class Field {
                value: OptionalText = undefined;
            }
            const field = new Field();
            console.log(field.value === undefined);
            field.value = "present";
            console.log(field.value);
            field.value = undefined;
            console.log(field.value === undefined);
            """, "true\npresent\ntrue\n"];
        yield return ["null-and-undefined", """
            class Field {
                value: string | null | undefined;
                constructor(value: any) { this.value = value; }
            }
            console.log(new Field(undefined).value === undefined);
            console.log(new Field(null).value === null);
            console.log(new Field("present").value);
            """, "true\ntrue\npresent\n"];
        yield return ["null-only-typed-control", """
            class Field {
                value: string | null;
                constructor(value: any) { this.value = value; }
            }
            const field = new Field(null);
            console.log(field.value === null);
            field.value = "present";
            console.log(field.value);
            field.value = null;
            console.log(field.value === null);
            """, "true\npresent\ntrue\n"];
        yield return ["optional-parameter-property", """
            class Field { constructor(public value?: string) {} }
            console.log(new Field().value === undefined);
            console.log(new Field(undefined).value === undefined);
            console.log(new Field("present").value);
            """, "true\ntrue\npresent\n"];
        yield return ["value-type-unions", """
            class Field {
                count: number | undefined;
                enabled: boolean | undefined;
                constructor(count: any, enabled: any) {
                    this.count = count;
                    this.enabled = enabled;
                }
            }
            const field = new Field(undefined, undefined);
            console.log(field.count === undefined, field.enabled === undefined);
            field.count = 7;
            field.enabled = false;
            console.log(field.count, field.enabled);
            field.count = undefined;
            field.enabled = undefined;
            console.log(field.count === undefined, field.enabled === undefined);
            """, "true true\n7 false\ntrue true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ClassFieldStoragePreservesUndefined(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, TestHarness.Run(source, mode));
    }
}
