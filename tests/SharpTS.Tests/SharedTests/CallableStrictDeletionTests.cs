using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class CallableStrictDeletionTests
{
    public const string IssueSource = """
        function declaration() {}
        const functions: any[] = [declaration, function named() {}, () => {}];
        for (const target of functions) {
            Object.defineProperty(target, "slot", { value: 7, configurable: false });
            function removeDot() { "use strict"; return delete target.slot; }
            function removeIndex() { "use strict"; return delete target["slot"]; }
            try { console.log("result", removeDot()); }
            catch (error) { console.log(error instanceof TypeError); }
            try { console.log("result", removeIndex()); }
            catch (error) { console.log(error instanceof TypeError); }
        }
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["ordinary-three-form-strict-delete-issue", IssueSource, "true\ntrue\ntrue\ntrue\ntrue\ntrue\n"];
        yield return ["own-data-accessor-refusal-preserves-descriptors-without-invocation", """
            function declaration() {}
            async function asyncDeclaration() {}
            const functions: any[] = [declaration, function() {}, () => {},
                asyncDeclaration, async function() {}, async () => {}];
            let reads = 0, writes = 0;
            const marker = {};
            const getter = function() { reads++; throw marker; };
            const setter = function(value: any) { writes++; };
            function dataDot(target: any) { "use strict"; return delete target.data; }
            function accessorDot(target: any) { "use strict"; return delete target.access; }
            function indexed(target: any, key: string) { "use strict"; return delete target[key]; }
            for (const target of functions) {
                Object.defineProperty(target, 'data', { value: 7, writable: true, enumerable: true, configurable: false });
                Object.defineProperty(target, 'access', { get: getter, set: setter, enumerable: true, configurable: false });
                try { console.log('result', dataDot(target)); } catch (error) { console.log(error instanceof TypeError); }
                try { console.log('result', indexed(target, 'data')); } catch (error) { console.log(error instanceof TypeError); }
                try { console.log('result', accessorDot(target)); } catch (error) { console.log(error instanceof TypeError); }
                try { console.log('result', indexed(target, 'access')); } catch (error) { console.log(error instanceof TypeError); }
                console.log(delete target.data, Reflect.deleteProperty(target, 'data'),
                    delete target['access'], Reflect.deleteProperty(target, 'access'));
                const data = Object.getOwnPropertyDescriptor(target, 'data')!;
                const access = Object.getOwnPropertyDescriptor(target, 'access')!;
                console.log(Object.hasOwn(target, 'data'), Object.hasOwn(target, 'access'),
                    data.value, data.writable, data.enumerable, data.configurable,
                    access.get === getter, access.set === setter, access.enumerable, access.configurable, reads, writes);
            }
            """, "true\ntrue\ntrue\ntrue\nfalse false false false\ntrue true 7 true true false true true true false 0 0\ntrue\ntrue\ntrue\ntrue\nfalse false false false\ntrue true 7 true true false true true true false 0 0\ntrue\ntrue\ntrue\ntrue\nfalse false false false\ntrue true 7 true true false true true true false 0 0\ntrue\ntrue\ntrue\ntrue\nfalse false false false\ntrue true 7 true true false true true true false 0 0\ntrue\ntrue\ntrue\ntrue\nfalse false false false\ntrue true 7 true true false true true true false 0 0\ntrue\ntrue\ntrue\ntrue\nfalse false false false\ntrue true 7 true true false true true true false 0 0\n"];
        // #2416 (P3) tracks virtual metadata integrity flags. Its original source is preserved
        // in artifacts/p2-goal/2415-metadata-preservation-boundary/original-fixture.ts.
        yield return ["sealed-frozen-metadata-and-synthetic-prototype-refusal", """
            function sealed(first: any, second: any) {}
            function frozen(first: any, second: any) {}
            const groups: any[] = [
                [sealed, function(first: any, second: any) {}, (first: any, second: any) => first],
                [frozen, function(first: any, second: any) {}, (first: any, second: any) => first]
            ];
            function nameDot(target: any) { "use strict"; return delete target.name; }
            function lengthDot(target: any) { "use strict"; return delete target.length; }
            function prototypeDot(target: any) { "use strict"; return delete target.prototype; }
            function indexed(target: any, key: string) { "use strict"; return delete target[key]; }
            for (let index = 0; index < 2; index++) {
                for (const target of groups[index]) {
                    const name = Object.getOwnPropertyDescriptor(target, 'name')!.value;
                    const length = Object.getOwnPropertyDescriptor(target, 'length')!.value;
                    Object.defineProperty(target, 'name', { value: name, writable: false, enumerable: false, configurable: true });
                    Object.defineProperty(target, 'length', { value: length, writable: false, enumerable: false, configurable: true });
                    if (index === 0) Object.seal(target); else Object.freeze(target);
                    console.log(index, Object.getOwnPropertyDescriptor(target, 'name')!.configurable,
                        Object.getOwnPropertyDescriptor(target, 'length')!.configurable);
                    try { console.log('result', nameDot(target)); } catch (error) { console.log(error instanceof TypeError); }
                    try { console.log('result', indexed(target, 'name')); } catch (error) { console.log(error instanceof TypeError); }
                    try { console.log('result', lengthDot(target)); } catch (error) { console.log(error instanceof TypeError); }
                    try { console.log('result', indexed(target, 'length')); } catch (error) { console.log(error instanceof TypeError); }
                    try { console.log('prototype-result', prototypeDot(target)); } catch (error) { console.log(error instanceof TypeError); }
                    try { console.log('prototype-result', indexed(target, 'prototype')); } catch (error) { console.log(error instanceof TypeError); }
                    console.log(delete target.name, Reflect.deleteProperty(target, 'length'),
                        Object.getOwnPropertyDescriptor(target, 'name')!.value === name,
                        Object.getOwnPropertyDescriptor(target, 'length')!.value === length,
                        Object.hasOwn(target, 'name'), Object.hasOwn(target, 'length'));
                }
            }
            """, "0 false false\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\nfalse false true true true true\n0 false false\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\nfalse false true true true true\n0 false false\ntrue\ntrue\ntrue\ntrue\nprototype-result true\nprototype-result true\nfalse false true true true true\n1 false false\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\nfalse false true true true true\n1 false false\ntrue\ntrue\ntrue\ntrue\ntrue\ntrue\nfalse false true true true true\n1 false false\ntrue\ntrue\ntrue\ntrue\nprototype-result true\nprototype-result true\nfalse false true true true true\n"];
        yield return ["configurable-data-accessor-cleanup-recreation-and-absent-success", """
            function declaration() {}
            async function asyncDeclaration() {}
            const functions: any[] = [declaration, function() {}, () => {},
                asyncDeclaration, async function() {}, async () => {}];
            let reads = 0;
            const marker = {};
            const getter = function() { reads++; throw marker; };
            function dataDot(target: any) { "use strict"; return delete target.data; }
            function accessorDot(target: any) { "use strict"; return delete target.access; }
            function indexed(target: any, key: string) { "use strict"; return delete target[key]; }
            for (const target of functions) {
                Object.defineProperty(target, 'data', { value: 7, configurable: true });
                Object.defineProperty(target, 'access', { get: getter, configurable: true });
                console.log(dataDot(target), indexed(target, 'access'), Object.hasOwn(target, 'data'),
                    Object.hasOwn(target, 'access'), Object.getOwnPropertyDescriptor(target, 'data') === undefined,
                    Object.getOwnPropertyDescriptor(target, 'access') === undefined, reads);
                Object.defineProperty(target, 'data', { value: 9, configurable: true });
                Object.defineProperty(target, 'access', { get: getter, configurable: true });
                console.log(Object.getOwnPropertyDescriptor(target, 'data')!.value, indexed(target, 'data'),
                    accessorDot(target), Object.hasOwn(target, 'data'), Object.hasOwn(target, 'access'), reads);
                Object.preventExtensions(target);
                console.log(dataDot(target), indexed(target, 'access'), Reflect.deleteProperty(target, 'data'),
                    Object.getOwnPropertyDescriptor(target, 'data') === undefined,
                    Object.getOwnPropertyDescriptor(target, 'access') === undefined, reads);
            }
            """, "true true false false true true 0\n9 true true false false 0\ntrue true true true true 0\ntrue true false false true true 0\n9 true true false false 0\ntrue true true true true 0\ntrue true false false true true 0\n9 true true false false 0\ntrue true true true true 0\ntrue true false false true true 0\n9 true true false false 0\ntrue true true true true 0\ntrue true false false true true 0\n9 true true false false 0\ntrue true true true true 0\ntrue true false false true true 0\n9 true true false false 0\ntrue true true true true 0\n"];
        yield return ["strict-dot-configurable-data-and-accessor-cleanup-issue2776", """
            function original() {}
            const target: any = original;
            Object.defineProperty(target, 'data', { value: 7, configurable: true });
            Object.defineProperty(target, 'access', { get() { return 9; }, configurable: true });
            function removeData() { 'use strict'; return delete target.data; }
            function removeAccessor() { 'use strict'; return delete target.access; }
            console.log(removeData(), Object.hasOwn(target, 'data'),
                Object.getOwnPropertyDescriptor(target, 'data') === undefined);
            console.log(removeAccessor(), Object.hasOwn(target, 'access'),
                Object.getOwnPropertyDescriptor(target, 'access') === undefined);
            """, "true false true\ntrue false true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void StrictCallableDeletionThrowsOnlyForExistingNonconfigurableProperties(
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
