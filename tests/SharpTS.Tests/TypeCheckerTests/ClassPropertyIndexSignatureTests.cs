using SharpTS.Diagnostics;
using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// #898: a class's own declared instance properties must be assignable to its own string index
/// signature (<b>TS2411</b> "Property 'X' of type '…' is not assignable to 'string' index type
/// '…'"). Previously only interfaces ran this check; classes — including generic classes whose
/// index type is an open type parameter — did not. Reported per-property at the property's line.
/// TS2411 is recorded (not thrown), so these assert on the diagnostic collection.
/// </summary>
public class ClassPropertyIndexSignatureTests
{
    private static IReadOnlyList<Diagnostic> Diagnostics(string source)
    {
        var tokens = new Lexer(source).ScanTokens();
        var parseResult = new Parser(tokens).Parse();
        Assert.True(parseResult.IsSuccess, "source should parse for a type-check test");
        return new TypeChecker(maxErrors: 50).CheckWithRecovery(parseResult.Statements).Diagnostics;
    }

    private static bool HasTs2411(IEnumerable<Diagnostic> diags) => diags.Any(d => d.TsCode == "TS2411");

    [Fact]
    public void Property_NotAssignableToOwnStringIndex_ReportsTS2411()
    {
        // `name: string` is not assignable to the class's own `number` string index.
        var diags = Diagnostics("""
            class A {
                [k: string]: number;
                name: string;
            }
            """);
        Assert.True(HasTs2411(diags));
    }

    [Fact]
    public void Property_AssignableToOwnStringIndex_NoError()
    {
        var diags = Diagnostics("""
            class A {
                [k: string]: number;
                count: number;
            }
            """);
        Assert.DoesNotContain(diags, d => d.TsCode == "TS2411");
    }

    [Fact]
    public void Property_AgainstAnyStringIndex_NoError()
    {
        // An `any` index accepts every property.
        var diags = Diagnostics("""
            class A {
                [k: string]: any;
                name: string;
            }
            """);
        Assert.DoesNotContain(diags, d => d.TsCode == "TS2411");
    }

    [Fact]
    public void NoIndexSignature_NoError()
    {
        var diags = Diagnostics("""
            class A {
                name: string;
            }
            """);
        Assert.DoesNotContain(diags, d => d.TsCode == "TS2411");
    }

    // ---- Generic classes: the index type is an open type parameter ----
    // Mirrors `subtypesOfTypeParameterWithConstraints` over base `class C3<T> { foo: T }`.

    [Fact]
    public void GenericProperty_NotAssignableToOwnTypeParameterIndex_ReportsTS2411()
    {
        // D3: own index `[x: string]: T`, `foo: U`. `U` is not assignable to `T` (T extends U).
        var diags = Diagnostics("""
            class C3<T> { foo: T; }
            class D3<T extends U, U> extends C3<T> {
                [x: string]: T;
                foo: U;
            }
            """);
        Assert.True(HasTs2411(diags));
    }

    [Fact]
    public void GenericProperty_AssignableToOwnTypeParameterIndex_NoError()
    {
        // D1: own index `[x: string]: T`, `foo: T` — assignable to itself.
        var diags = Diagnostics("""
            class C3<T> { foo: T; }
            class D1<T extends U, U> extends C3<T> {
                [x: string]: T;
                foo: T;
            }
            """);
        Assert.DoesNotContain(diags, d => d.TsCode == "TS2411");
    }

    [Fact]
    public void GenericProperty_ConstrainedToIndexParameter_NoError()
    {
        // D2: own index `[x: string]: U`, `foo: T` where T extends U — T is assignable to U.
        var diags = Diagnostics("""
            class C3<T> { foo: T; }
            class D2<T extends U, U> extends C3<U> {
                [x: string]: U;
                foo: T;
            }
            """);
        Assert.DoesNotContain(diags, d => d.TsCode == "TS2411");
    }
}
