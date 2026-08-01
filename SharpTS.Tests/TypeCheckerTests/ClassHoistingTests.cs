using SharpTS.Parsing;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// The plain <see cref="TypeChecker.Check(List{Stmt})"/> entry point must hoist class
/// declarations exactly like <see cref="TypeChecker.CheckWithRecovery(List{Stmt})"/> does,
/// so a function body can forward-reference a class declared later in the same file — a
/// common pattern in CJS libraries. Check() is the live path for workers and Test262.
/// </summary>
public class ClassHoistingTests
{
    private static List<Stmt> Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.ScanTokens();
        var parser = new Parser(tokens);
        return parser.ParseOrThrow();
    }

    [Fact]
    public void Check_ForwardClassValueReferenceFromFunctionBody_Passes()
    {
        var source = """
            function make(): Later {
                return new Later(7);
            }
            class Later {
                constructor(public n: number) {}
            }
            const item: Later = make();
            console.log(item.n);
            """;

        var typeMap = new TypeChecker().Check(Parse(source));

        Assert.NotNull(typeMap);
    }

    [Fact]
    public void Check_ForwardExportedClassValueReference_Passes()
    {
        var source = """
            function make(): Later {
                return new Later();
            }
            export class Later {}
            """;

        var typeMap = new TypeChecker().Check(Parse(source));

        Assert.NotNull(typeMap);
    }
}
