using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class OptionalParameterPropertyTests
{
    public const string IssueSource = """
        function kind(value: any): string {
            return value === undefined ? "missing" : value === null ? "null" : value;
        }
        class Base { constructor(public value?: string) {} }
        class Derived extends Base {
            constructor(value?: string, public label: string = "fallback", ...tail: string[]) {
                super(value);
                console.log(kind(value), kind(this.value), kind(this.label), tail.length);
            }
        }
        new Derived();
        new Derived(undefined, undefined);
        new Derived(null as any, null as any, "tail");
        new Derived("ok", "named", "x", "y");
        """;

    public static TheoryData<string> AcceptedSources => new()
    {
        IssueSource,
        """
        const Box = class { constructor(public value?: string) {} };
        const box = new Box();
        box.value = undefined;
        box.value = "present";
        """,
        """
        class Box<T> { constructor(public value?: T) {} }
        const box = new Box<string>();
        box.value = undefined;
        box.value = "present";
        """,
        """
        class Handler { constructor(public invoke?: () => string) {} }
        const handler = new Handler(() => "present");
        handler.invoke = undefined;
        """,
        """
        class Box { constructor(readonly value?: string) {} }
        const box = new Box();
        const value: string | undefined = box.value;
        """,
        """
        class Box {
            other: string = "present";
            constructor(public value: Box["other"], public optional?: Box["other"]) {}
        }
        const box = new Box("present");
        box.optional = undefined;
        """,
    };

    [Theory, MemberData(nameof(AcceptedSources))]
    public void OptionalParameterPropertiesIncludeUndefined(string source)
    {
        foreach (bool exactOptionalPropertyTypes in new[] { false, true })
        {
            var checker = new TypeChecker(TypeCheckerOptions.Strict with
            {
                ExactOptionalPropertyTypes = exactOptionalPropertyTypes,
            });
            var result = checker.CheckWithRecovery(TestHarness.ParseOrThrow(source));
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SynthesizedFieldsPreserveParameterPropertyModifiers(bool expression)
    {
        const string members = "constructor(private readonly required: string, public defaulted: string = 'fallback', protected readonly optional?: string) {}";
        var statement = Assert.Single(TestHarness.ParseOrThrow(expression
            ? $"const Box = class {{ {members} }};"
            : $"class Box {{ {members} }}"));
        var fields = expression
            ? Assert.IsType<Expr.ClassExpr>(Assert.IsType<Stmt.Const>(statement).Initializer).Fields
            : Assert.IsType<Stmt.Class>(statement).Fields;

        Assert.Collection(fields,
            required =>
            {
                Assert.Equal("string", required.TypeAnnotation);
                Assert.False(required.IsOptional);
                Assert.True(required.IsReadonly);
                Assert.Equal(AccessModifier.Private, required.Access);
            },
            defaulted =>
            {
                Assert.Equal("string", defaulted.TypeAnnotation);
                Assert.False(defaulted.IsOptional);
                Assert.False(defaulted.IsReadonly);
                Assert.Equal(AccessModifier.Public, defaulted.Access);
            },
            optional =>
            {
                Assert.Equal("(string) | undefined", optional.TypeAnnotation);
                Assert.True(optional.IsOptional);
                Assert.True(optional.IsReadonly);
                Assert.Equal(AccessModifier.Protected, optional.Access);
            });
    }

    public static TheoryData<string, string> RejectedSources => new()
    {
        {
            """
            class Box { constructor(public value?: string) {} }
            const value: string = new Box().value;
            """,
            "TS2322"
        },
        {
            """
            class Box { constructor(public value?: string) {} }
            new Box().value = 1;
            """,
            "TS2322"
        },
        {
            """
            class Box { constructor(public value: string) {} }
            new Box("present").value = undefined;
            """,
            "TS2322"
        },
        {
            """
            class Box { constructor(public value: string = "present") {} }
            new Box().value = undefined;
            """,
            "TS2322"
        },
        {
            """
            class Box { constructor(readonly value?: string) {} }
            new Box().value = "present";
            """,
            "TS2540"
        },
    };

    [Theory, MemberData(nameof(RejectedSources))]
    public void ParameterPropertyTypesAndReadonlyChecksRemainEnforced(string source, string code)
    {
        var result = new TypeChecker(TypeCheckerOptions.Strict).CheckWithRecovery(TestHarness.ParseOrThrow(source));
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal(code, diagnostic.TsCode);
        Assert.Equal(2, diagnostic.Line);
    }
}
