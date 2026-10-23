using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassExpressionInferredNameTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["anonymous-variable-class-name-issue", """
            const Inferred = class {};
            const Explicit = class Internal {};
            const inferred: any = Inferred, explicit: any = Explicit;
            console.log(inferred.name, explicit.name);
            """, "Inferred Internal\n"];
        yield return ["explicit-generic-self-binding-and-native-anonymous-control", """
            const Outside = class Internal<T> {
                value: T;
                constructor(value: T) { this.value = value; }
                static self() { return Internal; }
            };
            const Native = class extends Array<number> {};
            console.log((Outside as any).name, Outside.self() === Outside, new Outside<number>(7).value);
            const values = new Native(3, 5);
            console.log((Native as any).name, values.length, values[0], values[1], values instanceof Native);
            """, "Internal true 7\nNative 2 3 5 true\n"];
        yield return ["inferred-name-is-visible-during-static-initialization", """
            const events: string[] = [];
            const Inferred = class {
                static observed = (this as any).name;
                static { events.push('block:' + (this as any).name); }
            };
            const Explicit = class Internal {
                static observed = (this as any).name;
                static { events.push('explicit:' + (this as any).name); }
            };
            console.log((Inferred as any).name, Inferred.observed, (Explicit as any).name, Explicit.observed);
            console.log(events.join(','));
            """, "Inferred Inferred Internal Internal\nblock:Inferred,explicit:Internal\n"];
        yield return ["repeated-factory-keeps-inferred-name-and-fresh-constructor-state", """
            function make(seed: number) {
                const Product = class {
                    value = seed;
                    static observed = (this as any).name;
                    read() { return this.value; }
                };
                return Product;
            }
            const First = make(3), Second = make(5);
            const first = new First(), second = new Second();
            console.log((First as any).name, (Second as any).name, First.observed, Second.observed, First === Second);
            console.log(first.read(), second.read(), First.prototype === Second.prototype,
                first.constructor === First, second.constructor === Second);
            """, "Product Product Product Product false\n3 5 false true true\n"];
        yield return ["static-own-name-members-override-inferred-metadata", """
            const nameKey = 'name';
            const Data = class {
                static [nameKey] = 'own-data';
                static observed = (this as any).name;
            };
            const Method = class {
                static [nameKey]() { return 7; }
                static observed = typeof (this as any).name;
            };
            const Accessor = class {
                static get [nameKey]() { return 'own-accessor'; }
                static observed = (this as any).name;
            };
            const data: any = Data, method: any = Method, accessor: any = Accessor;
            console.log(data.name, Data.observed, Object.getOwnPropertyDescriptor(data, 'name')!.enumerable);
            console.log(method.name(), Method.observed, typeof Object.getOwnPropertyDescriptor(method, 'name')!.value);
            console.log(accessor.name, Accessor.observed, typeof Object.getOwnPropertyDescriptor(accessor, 'name')!.get);
            """, "own-data own-data true\n7 function function\nown-accessor own-accessor function\n"];
        yield return ["variable-kinds-parentheses-and-name-descriptor-views", """
            const Constant = class {};
            let Mutable = (class {});
            var Variable = ((class {}));
            const Explicit = (class Internal {});
            for (const target of [Constant, Mutable, Variable, Explicit]) {
                const descriptor = Object.getOwnPropertyDescriptor(target, 'name')!;
                console.log(target.name, Reflect.get(target, 'name'), descriptor.value,
                    descriptor.writable, descriptor.enumerable, descriptor.configurable, Object.hasOwn(target, 'name'));
            }
            """, "Constant Constant Constant false false true true\nMutable Mutable Mutable false false true true\nVariable Variable Variable false false true true\nInternal Internal Internal false false true true\n"];
        yield return ["variable-kinds-bare-anonymous-initializers", """
            const Constant = class {};
            let Mutable = class {};
            var Variable = class {};
            const Explicit = (class Internal {});
            for (const target of [Constant, Mutable, Variable, Explicit]) {
                const descriptor = Object.getOwnPropertyDescriptor(target, 'name')!;
                console.log(target.name, Reflect.get(target, 'name'), descriptor.value,
                    descriptor.writable, descriptor.enumerable, descriptor.configurable, Object.hasOwn(target, 'name'));
            }
            """, "Constant Constant Constant false false true true\nMutable Mutable Mutable false false true true\nVariable Variable Variable false false true true\nInternal Internal Internal false false true true\n"];
        yield return ["nested-and-duplicate-var-initializers-in-sync-and-async-bodies", """
            function checkSync() {
                var Chosen: any = class {
                    static observed = (this as any).name;
                    value = 1;
                };
                const first = Chosen;
                {
                    var Chosen: any = class {
                        static observed = (this as any).name;
                        value = 2;
                    };
                }
                console.log(first.name, first.observed, Chosen.name, Chosen.observed, first === Chosen);
                console.log(new first().value, new Chosen().value,
                    first.prototype.constructor === first, Chosen.prototype.constructor === Chosen);
            }
            async function checkAsync() {
                var Pending: any;
                await 0;
                {
                    var Pending: any = class {
                        static observed = (this as any).name;
                        value = 3;
                    };
                }
                const first = Pending;
                {
                    var Pending: any = class {
                        static observed = (this as any).name;
                        value = 4;
                    };
                }
                console.log(first.name, first.observed, Pending.name, Pending.observed, first === Pending);
                console.log(new first().value, new Pending().value,
                    first.prototype.constructor === first, Pending.prototype.constructor === Pending);
            }
            checkSync();
            checkAsync();
            """, "Chosen Chosen Chosen Chosen false\n1 2 true true\nPending Pending Pending Pending false\n3 4 true true\n"];
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
    public void VariableInitializersInferPublicNamesBeforeStaticInitialization(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (name is "variable-kinds-parentheses-and-name-descriptor-views"
            or "nested-and-duplicate-var-initializers-in-sync-and-async-bodies")
        {
            // Preserve Cases while matching the exact historical LF/finalLF source at execution.
            source = source.ReplaceLineEndings("\n") + "\n";
            Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
                .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        }
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
