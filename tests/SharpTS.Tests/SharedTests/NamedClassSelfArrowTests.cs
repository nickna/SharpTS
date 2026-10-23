using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class NamedClassSelfArrowTests
{
    public const string OriginalFieldSource = """
        const C = class Named { static read = () => Named; };
        console.log(C.read() === C);
        """;

    public const string OriginalShadowedGetterSource = """
        const Named = "outside";
        const C = class Named {
            static get self() { const read = () => Named; return read(); }
        };
        console.log(C.self === C, Named === "outside");
        """;

    public const string GetterWithoutOuterSource = """
        const C = class Named {
            static get self() { const read = () => Named; return read(); }
        };
        console.log(C.self === C);
        """;

    public const string PerEvaluationSource = """
        function create() {
            return class Named {
                static read = () => Named;
                read = () => Named;
            };
        }
        const A = create(), B = create();
        console.log(A !== B, A.read() === A, B.read() === B,
            new A().read() === A, new B().read() === B);
        """;

    public const string MemberContextsSource = """
        const Named = "outside";
        const Instance = class Named { read = () => Named; };
        const Method = class Named {
            static read() { const arrow = () => Named; return arrow(); }
            static get direct() { return Named; }
        };
        const borrowed = Method.read;
        console.log(new Instance().read() === Instance, Method.read() === Method,
            Method.direct === Method, Named === "outside");
        console.log(borrowed.call(Instance) === Method);
        """;

    public const string LexicalShadowSource = """
        const Named = "outside";
        const C = class Named {
            static read = () => { const Named = "local"; return Named; };
            static self = () => Named;
        };
        console.log(C.read(), C.self() === C, Named);
        """;

    public const string LocalShadowSource = """
        const Named = "outside";
        const C = class Named { static read = () => { const Named = "local"; return Named; }; };
        console.log(C.read(), Named);
        """;

    public const string CapturedShadowSource = """
        const Named = "outside";
        const C = class Named {
            static make() {
                let Named: any = "before";
                const read = () => Named;
                Named = "after";
                return read;
            }
        };
        console.log(C.make()(), Named);
        """;

    public const string TypedConstructorShadowSource = """
        const C = class Named {
            static make() {
                let Named: typeof C = C;
                const read = () => Named;
                Named = null as any;
                return read;
            }
        };
        console.log(C.make()() === null);
        """;

    public const string AccessorScopesSource = """
        const Getter = class Named {
            static get read(): string {
                const Named = "local";
                const read = () => Named;
                return read();
            }
        };
        console.log(Getter.read);
        let result = "";
        const Setter = class Named {
            static set value(Named: string) {
                const read = () => Named;
                result = read();
            }
        };
        Setter.value = "parameter";
        console.log(result);
        const Method = class Named {
            static Named() { return () => Named; }
        };
        console.log(Method.Named()() === Method);
        """;

    public const string BlockAndDefaultScopesSource = """
        let localResult = "";
        const Local = class Named {
            static {
                const Named = "block";
                const read = () => Named;
                localResult = read();
            }
        };
        console.log(localResult);
        const Default = class Named {
            static make(read: () => unknown = () => Named) {
                var Named = "body";
                return [read, () => Named];
            }
        };
        const pair = Default.make();
        console.log(pair[0]() === Default, pair[1]() === "body");
        """;

    public const string HoistedStaticBlockVarSource = """
        let result = "";
        const C = class Named {
            static {
                const read = () => Named;
                var Named = "block-var";
                result = read();
            }
        };
        console.log(result);
        """;

    public const string OwnerRelaySource = """
        function create(Named: string) {
            const outsideRead = () => Named;
            let C = class Named {
                static read = () => () => Named;
                static make(Named: string) { return () => Named; }
                static Inner = class Inner {
                    static own = () => Inner;
                    static outer = () => Named;
                };
            };
            const owner = C, self = C.read(), shadow = C.make("parameter");
            C = null as any;
            console.log(self() === owner, shadow() === "parameter", outsideRead(),
                owner.Inner.own() === owner.Inner, owner.Inner.outer() === owner);
            return owner;
        }
        const First = create("first"), Second = create("second");
        console.log(First !== Second);
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["issue-field-arrow", OriginalFieldSource, "true\n"];
        yield return ["issue-shadowed-getter-arrow", OriginalShadowedGetterSource, "true true\n"];
        yield return ["getter-without-outer-name", GetterWithoutOuterSource, "true\n"];
        yield return ["per-evaluation-static-and-instance-self", PerEvaluationSource, "true true true true true\n"];
        yield return ["member-contexts-and-borrowed-receiver", MemberContextsSource, "true true true true\ntrue\n"];
        yield return ["local-shadow-alongside-genuine-self", LexicalShadowSource, "local true outside\n"];
        yield return ["isolated-local-shadow", LocalShadowSource, "local outside\n"];
        yield return ["isolated-mutable-captured-shadow", CapturedShadowSource, "after outside\n"];
        yield return ["isolated-typed-constructor-shadow", TypedConstructorShadowSource, "true\n"];
        yield return ["accessor-scopes-and-method-property-name", AccessorScopesSource, "local\nparameter\ntrue\n"];
        yield return ["static-block-and-default-parameter-scopes", BlockAndDefaultScopesSource, "block\ntrue true\n"];
        yield return ["nested-owner-relay-and-reassigned-receiver", OwnerRelaySource, "true true first true true\ntrue true second true true\ntrue\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void NamedClassSelfArrowsUseTheirLexicalOwnerAndPreserveRealShadows(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Equal(expected, mode == ExecutionMode.Interpreted
            ? RunResolvedInterpreter(source)
            : TestHarness.Run(source, mode));
    }

    [Fact]
    public void HoistedStaticBlockVarPreservesItsActualCapturedBinding()
    {
        Assert.Equal("block-var\n", RunResolvedInterpreter(HoistedStaticBlockVarSource));
        var (errors, output) = TestHarness.CompileVerifyAndRun(HoistedStaticBlockVarSource);
        Assert.Empty(errors);
        Assert.Equal("block-var\n", output);
    }

    private static string RunResolvedInterpreter(string source)
    {
        // Match the checked CLI path; raw unresolved default-scope lookup is tracked by #2453.
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        using var output = new StringWriter();
        using var errors = new StringWriter();
        using var interpreter = new Interpreter(output, errors);
        new VariableResolver(interpreter).Resolve(statements);
        interpreter.Interpret(statements, typeMap);
        Assert.Empty(errors.ToString());
        Assert.Null(interpreter.LastUncaughtError);
        return output.ToString().Replace("\r\n", "\n");
    }
}
