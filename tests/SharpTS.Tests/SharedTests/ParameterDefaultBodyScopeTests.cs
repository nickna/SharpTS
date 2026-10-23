using SharpTS.Execution;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ParameterDefaultBodyScopeTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["sync-forms-default-outer-binding", """
            const value = "outside";
            function declaration(read: any = () => value) { var value = "declaration"; return [read, () => value]; }
            const expression = function(read: any = () => value) { var value = "expression"; return [read, () => value]; };
            const arrow = (read: any = () => value) => { var value = "arrow"; return [read, () => value]; };
            const object = { make(read: any = () => value) { var value = "object"; return [read, () => value]; } };
            class Owner {
                make(read: any = () => value) { var value = "instance"; return [read, () => value]; }
                static make(read: any = () => value) { var value = "static"; return [read, () => value]; }
            }
            for (const make of [declaration, expression, arrow, () => object.make(), () => new Owner().make(), () => Owner.make()]) {
                const pair: any = make();
                console.log(pair[0](), pair[1]());
            }
            """, "outside declaration\noutside expression\noutside arrow\noutside object\noutside instance\noutside static\n"];
        yield return ["parameter-var-copy-and-simple-sharing", """
            function split(value: any = 1, read: any = () => value, write: any = (next: any) => value = next) {
                var value;
                console.log("initial", value, read());
                value = 2;
                console.log("body", value, read());
                write(3);
                console.log("parameter", value, read());
            }
            split();
            function simple(value: any) {
                var value; const read = () => value;
                console.log("simple-initial", value, read());
                value = 4; console.log("simple", value, read());
            }
            simple(1);
            function rest(...items: any[]) {
                var items: any[]; const read = () => items;
                console.log("rest-initial", items.join(","), read() === items);
                items = [5]; console.log("rest", items[0], read()[0]);
            }
            rest(1, 2);
            function noBodyVar(value: any = 1, read: any = () => value) { value = 2; console.log("no-body-var", read()); }
            noBodyVar();
            """, "initial 1 1\nbody 2 1\nparameter 2 3\nsimple-initial 1 1\nsimple 4 4\nrest-initial 1,2 true\nrest 5 5\nno-body-var 2\n"];
        yield return ["constructor-default-outer-binding", """
            const value = "outside";
            class Base {
                read: any; body: any;
                constructor(read: any = () => value) { var value = "base"; this.read = read; this.body = () => value; }
            }
            class Empty {}
            class Derived extends Empty {
                read: any; body: any;
                constructor(read: any = () => value) { super(); var value = "derived"; this.read = read; this.body = () => value; }
            }
            const base = new Base(), derived = new Derived();
            console.log(base.read(), base.body(), derived.read(), derived.body());
            """, "outside base outside derived\n"];
        yield return ["async-and-generator-default-outer-binding", """
            const value = "outside";
            async function asyncDeclaration(read: any = () => value) { var value = "async-declaration"; await Promise.resolve(); return [read, () => value]; }
            const asyncExpression = async function(read: any = () => value) { var value = "async-expression"; await Promise.resolve(); return [read, () => value]; };
            const asyncArrow = async (read: any = () => value) => { var value = "async-arrow"; await Promise.resolve(); return [read, () => value]; };
            function* generatorDeclaration(read: any = () => value) { var value = "generator-declaration"; yield [read, () => value]; }
            const generatorExpression = function*(read: any = () => value) { var value = "generator-expression"; yield [read, () => value]; };
            async function* asyncGeneratorDeclaration(read: any = () => value) { var value = "async-generator-declaration"; await Promise.resolve(); yield [read, () => value]; }
            const asyncGeneratorExpression = async function*(read: any = () => value) { var value = "async-generator-expression"; await Promise.resolve(); yield [read, () => value]; };
            async function run() {
                const first: any = await asyncDeclaration(); console.log(first[0](), first[1]());
                const second: any = await asyncExpression(); console.log(second[0](), second[1]());
                const third: any = await asyncArrow(); console.log(third[0](), third[1]());
                const fourth: any = generatorDeclaration().next().value; console.log(fourth[0](), fourth[1]());
                const fifth: any = generatorExpression().next().value; console.log(fifth[0](), fifth[1]());
                const sixth: any = (await asyncGeneratorDeclaration().next()).value; console.log(sixth[0](), sixth[1]());
                const seventh: any = (await asyncGeneratorExpression().next()).value; console.log(seventh[0](), seventh[1]());
            }
            run();
            """, "outside async-declaration\noutside async-expression\noutside async-arrow\noutside generator-declaration\noutside generator-expression\noutside async-generator-declaration\noutside async-generator-expression\n"];
        yield return ["named-class-self-default-before-body-var", """
            const C = class Named {
                static make(read: any = () => Named) {
                    var Named = "body";
                    return [read, () => Named];
                }
            };
            const pair: any = C.make();
            console.log(pair[0]() === C, pair[1]() === "body");
            """, "true true\n"];
        yield return ["body-lexical-tdz-and-hoisted-declarations", """
            const value = "outside";
            function lexical(read: any = () => value) {
                const body = () => value;
                try { console.log(body()); } catch (error: any) { console.log(error.name); }
                let value = "lexical";
                console.log(read(), body());
            }
            lexical();
            function helper() { return "outside-helper"; }
            function hoistedFunction(read: any = () => helper) {
                console.log(read()(), helper());
                function helper() { return "body-helper"; }
            }
            hoistedFunction();
            function hoistedVar(read: any = () => value) {
                console.log(read(), value === undefined);
                var value = "body";
                console.log(read(), value);
            }
            hoistedVar();
            """, "ReferenceError\noutside lexical\noutside-helper body-helper\noutside true\noutside body\n"];
        yield return ["destructured-defaults-copy-and-order", """
            const value = "outside";
            function helper() { return "outer-helper"; }
            const arrayTrace: string[] = [];
            function array([read = (arrayTrace.push("array"), () => value)]: any[] = [],
                later: any = (arrayTrace.push("later"), read), getHelper: any = () => helper) {
                var read;
                var value = "array-body";
                function helper() { return "body-helper"; }
                arrayTrace.push("body");
                console.log(read === later, later(), getHelper()(), helper(), arrayTrace.join(","));
                read = () => value;
                console.log(later(), read());
            }
            array();
            const objectTrace: string[] = [];
            function object({ nested: { read = (objectTrace.push("nested"), () => value) } }: any = { nested: {} },
                later: any = (objectTrace.push("later"), read)) {
                var read;
                var value = "object-body";
                objectTrace.push("body");
                console.log(read === later, later(), objectTrace.join(","));
                read = () => value;
                console.log(later(), read());
            }
            object();
            """, "true outside outer-helper body-helper array,later,body\noutside array-body\ntrue outside nested,later,body\noutside object-body\n"];
        yield return ["async-uninitialized-var-preserves-parameter", """
            async function run(value: any) { var value; console.log(value); }
            run(7);
            """, "7\n"];
        yield return ["enclosing-local-default-outer-binding", """
            function enclosing() {
                const value = "enclosing";
                function make(read: any = () => value) { var value = "body"; return [read, () => value]; }
                const pair: any = make();
                console.log(pair[0](), pair[1]());
            }
            enclosing();
            """, "enclosing body\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ParameterDefaultsKeepTheirOwnScope(string name, string source, string expected, ExecutionMode mode)
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
            Assert.Equal(expected, TestHarness.Run(source, mode));
        }
    }

    [Theory, MemberData(nameof(Cases))]
    public void ResolvedDefaultsAndBodiesMatchRawInterpretation(string name, string source, string expected)
    {
        Assert.NotEmpty(name);
        var statements = new Parser(new Lexer(source).ScanTokens()).ParseOrThrow();
        var typeMap = new TypeChecker().Check(statements);
        using var output = new StringWriter();
        using var interpreter = new Interpreter(stdout: output, stderr: TextWriter.Null);
        new VariableResolver(interpreter).Resolve(statements);
        interpreter.Interpret(statements, typeMap);
        Assert.Null(interpreter.LastUncaughtError);
        Assert.Equal(expected, output.ToString().Replace("\r\n", "\n"));
    }

}
