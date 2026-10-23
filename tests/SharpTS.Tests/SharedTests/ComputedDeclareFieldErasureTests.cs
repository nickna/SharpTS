using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ComputedDeclareFieldErasureTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["computed-declare-expression-issue", """
            let reads = 0;
            const keys = {
                get erased(): "erased" { reads++; return "erased"; }
            };
            const Model = class {
                declare [keys.erased]: number;
                actual = 1;
            };
            const value = new Model();
            console.log(reads, Object.hasOwn(value, "erased"), value.actual);
            """, "0 false 1\n"];
        yield return ["computed-declare-declaration-counterpart", """
            let reads = 0;
            const keys = {
                get erased(): "erased" { reads++; return "erased"; }
            };
            class Model {
                declare [keys.erased]: number;
                actual = 1;
            }
            const value = new Model();
            console.log(reads, Object.hasOwn(value, "erased"), value.actual);
            """, "0 false 1\n"];
        yield return ["static-readonly-computed-declarations-erase-in-both-class-forms", """
            let reads = 0;
            const keys = {
                get first(): 'first' { reads++; return 'first'; },
                get second(): 'second' { reads++; return 'second'; }
            };
            class Declaration {
                declare static readonly [keys.first]: number;
                static actual = 3;
            }
            const Expression = class {
                declare static readonly [keys.second]: string;
                static actual = 5;
            };
            console.log(reads, Object.hasOwn(Declaration, 'first'), Declaration.actual);
            console.log(reads, Object.hasOwn(Expression, 'second'), Expression.actual);
            """, "0 false 3\n0 false 5\n"];
        yield return ["literal-and-explicit-unique-symbol-readonly-declarations-erase", """
            const key: unique symbol = Symbol('erased');
            class Declaration {
                declare readonly ['literal']: number;
                declare readonly [key]: string;
                actual = 3;
            }
            const Expression = class {
                declare readonly ['literal']: number;
                declare readonly [key]: string;
                actual = 5;
            };
            const first = new Declaration(), second = new Expression();
            console.log(Object.hasOwn(first, 'literal'), Object.hasOwn(first, key), Reflect.ownKeys(first).length, first.actual);
            console.log(Object.hasOwn(second, 'literal'), Object.hasOwn(second, key), Reflect.ownKeys(second).length, second.actual);
            """, "false false 1 3\nfalse false 1 5\n"];
        yield return ["ordinary-computed-key-and-field-initializer-order-survive-erasure", """
            const events: string[] = [];
            const keys = {
                get erased(): 'erased' { events.push('erased-key'); return 'erased'; },
                get actual(): 'actual' { events.push('actual-key'); return 'actual'; }
            };
            const Model = class {
                before = (events.push('before'), 1);
                declare [keys.erased]: number;
                [keys.actual] = (events.push('actual-value'), 2);
                after = (events.push('after'), 3);
            };
            console.log(events.join(','));
            events.length = 0;
            const first = new Model(), second = new Model();
            console.log(events.join(','));
            console.log(Object.hasOwn(first, 'erased'), Object.hasOwn(second, 'erased'), Object.keys(first).join(','), first.before, first.after);
            """, "actual-key\nbefore,actual-value,after,before,actual-value,after\nfalse false before,actual,after 1 3\n"];
        yield return ["static-readonly-computed-expression-declaration-erases", """
            let reads = 0;
            const keys = {
                get first(): 'first' { reads++; return 'first'; },
                get second(): 'second' { reads++; return 'second'; }
            };
            const Expression = class {
                declare static readonly [keys.second]: string;
                static actual = 5;
            };
            console.log(reads, Object.hasOwn(Expression, 'second'), Expression.actual);
            """, "0 false 5\n"];
        yield return ["ordinary-computed-key-and-field-initializer-order-with-presence-controls", """
            const events: string[] = [];
            const keys = {
                get erased(): 'erased' { events.push('erased-key'); return 'erased'; },
                get actual(): 'actual' { events.push('actual-key'); return 'actual'; }
            };
            const Model = class {
                before = (events.push('before'), 1);
                declare [keys.erased]: number;
                [keys.actual] = (events.push('actual-value'), 2);
                after = (events.push('after'), 3);
            };
            console.log(events.join(','));
            events.length = 0;
            const first = new Model(), second = new Model();
            console.log(events.join(','));
            console.log(Object.hasOwn(first, 'erased'), Object.hasOwn(second, 'erased'), Object.hasOwn(first, 'before'), Object.hasOwn(first, 'actual'), Object.hasOwn(first, 'after'), first.before, first.after);
            """, "actual-key\nbefore,actual-value,after,before,actual-value,after\nfalse false true true true 1 3\n"];
    }

    public static IEnumerable<object[]> SavedCases() => Cases();

    public static IEnumerable<object[]> ModeCases()
    {
        foreach (var row in Cases())
            yield return [row[0], row[1], row[2], ExecutionMode.Interpreted];
        foreach (var row in SavedCases())
            yield return [row[0], row[1], row[2], ExecutionMode.Compiled];
    }

    [Theory, MemberData(nameof(ModeCases))]
    public void ComputedDeclarationsDoNotEvaluateKeysOrCreateRuntimeFields(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
