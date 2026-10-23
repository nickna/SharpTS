using SharpTS.Execution;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class CallableAbsentDeletionTests
{
    public const string IssueSource = """
        function original() {}
        const target: any = original;
        Object.freeze(target);
        console.log(delete target["missing"], Reflect.deleteProperty(target, "missing"),
            Object.hasOwn(target, "missing"));
        """;

    public static IEnumerable<object[]> Cases()
    {
        yield return ["frozen-ordinary-absent-index-issue", IssueSource, "true true false\n"];
        yield return ["ordinary-forms-integrity-preserves-absence-and-own-refusal", """
            function extensible() {}
            function sealed() {}
            function frozen() {}
            const groups: any[] = [
                [extensible, function() {}, () => {}],
                [sealed, function() {}, () => {}],
                [frozen, function() {}, () => {}]
            ];
            for (let index = 0; index < 3; index++) {
                for (const target of groups[index]) {
                    Object.defineProperty(target, 'slot', { value: 7, writable: true, configurable: true });
                    Object.defineProperty(target, 'locked', { value: 9, configurable: false });
                    if (index === 0) Object.preventExtensions(target);
                    else if (index === 1) Object.seal(target); else Object.freeze(target);
                    console.log(index, delete target.missing, delete target['missing'],
                        Reflect.deleteProperty(target, 'missing'), Object.hasOwn(target, 'missing'),
                        Object.getOwnPropertyDescriptor(target, 'missing') === undefined);
                    console.log(delete target.slot, delete target['locked'], Reflect.deleteProperty(target, 'locked'),
                        Object.getOwnPropertyDescriptor(target, 'locked')!.value, Object.hasOwn(target, 'slot'),
                        Object.getOwnPropertyDescriptor(target, 'locked')!.configurable);
                    const ownPrototype = Object.hasOwn(target, 'prototype');
                    console.log(delete target.prototype, delete target['prototype'],
                        Reflect.deleteProperty(target, 'prototype'), Object.hasOwn(target, 'prototype') === ownPrototype);
                }
            }
            """, "0 true true true false true\ntrue false false 9 false false\nfalse false false true\n0 true true true false true\ntrue false false 9 false false\nfalse false false true\n0 true true true false true\ntrue false false 9 false false\ntrue true true true\n1 true true true false true\nfalse false false 9 true false\nfalse false false true\n1 true true true false true\nfalse false false 9 true false\nfalse false false true\n1 true true true false true\nfalse false false 9 true false\ntrue true true true\n2 true true true false true\nfalse false false 9 true false\nfalse false false true\n2 true true true false true\nfalse false false 9 true false\nfalse false false true\n2 true true true false true\nfalse false false 9 true false\ntrue true true true\n"];
        yield return ["strict-absent-dot-index-reflect-across-callable-forms", """
            function declaration() {}
            async function asyncDeclaration() {}
            const functions: any[] = [declaration, function() {}, () => {},
                asyncDeclaration, async function() {}, async () => {}];
            function removeDot(target: any) { "use strict"; return delete target.missing; }
            function removeIndex(target: any) { "use strict"; return delete target['missing']; }
            function removeName(target: any) { "use strict"; return delete target.name; }
            function removeLength(target: any) { "use strict"; return delete target.length; }
            function removeMetadataIndex(target: any, key: string) { "use strict"; return delete target[key]; }
            for (const target of functions) {
                console.log(delete target.name, delete target['length']);
                Object.freeze(target);
                console.log(removeDot(target), removeIndex(target), Reflect.deleteProperty(target, 'missing'),
                    Object.hasOwn(target, 'missing'), Object.getOwnPropertyDescriptor(target, 'missing') === undefined,
                    Object.isFrozen(target));
                console.log(removeName(target), removeMetadataIndex(target, 'name'), Reflect.deleteProperty(target, 'name'),
                    Object.hasOwn(target, 'name'), Object.getOwnPropertyDescriptor(target, 'name') === undefined,
                    removeLength(target), removeMetadataIndex(target, 'length'), Reflect.deleteProperty(target, 'length'),
                    Object.hasOwn(target, 'length'), Object.getOwnPropertyDescriptor(target, 'length') === undefined);
            }
            """, "true true\ntrue true true false true true\ntrue true true false true true true true false true\ntrue true\ntrue true true false true true\ntrue true true false true true true true false true\ntrue true\ntrue true true false true true\ntrue true true false true true true true false true\ntrue true\ntrue true true false true true\ntrue true true false true true true true false true\ntrue true\ntrue true true false true true\ntrue true true false true true true true false true\ntrue true\ntrue true true false true true\ntrue true true false true true true true false true\n"];
        yield return ["inherited-nonconfigurable-accessor-is-not-an-own-delete", """
            function declaration() {}
            async function asyncDeclaration() {}
            const functions: any[] = [declaration, function() {}, () => {},
                asyncDeclaration, async function() {}, async () => {}];
            let gets = 0;
            const marker = {};
            const selectedPrototype: any = Object.create(Function.prototype);
            const getter = function() { gets++; throw marker; };
            Object.defineProperty(selectedPrototype, 'inherited', { get: getter, enumerable: true, configurable: false });
            function removeDot(target: any) { "use strict"; return delete target.inherited; }
            function removeIndex(target: any) { "use strict"; return delete target['inherited']; }
            for (const target of functions) {
                Object.setPrototypeOf(target, selectedPrototype);
                Object.defineProperty(target, 'own', { value: 7, configurable: true });
                Object.freeze(target);
                console.log(removeDot(target), removeIndex(target), Reflect.deleteProperty(target, 'inherited'),
                    Object.hasOwn(target, 'inherited'), 'inherited' in target,
                    Object.getOwnPropertyDescriptor(target, 'inherited') === undefined);
                console.log(delete target.own, delete target['own'], Reflect.deleteProperty(target, 'own'),
                    Object.hasOwn(target, 'own'), Object.getOwnPropertyDescriptor(target, 'own')!.value,
                    Object.getOwnPropertyDescriptor(selectedPrototype, 'inherited')!.get === getter, gets);
            }
            """ + "\r\n", "true true true false true true\nfalse false false true 7 true 0\ntrue true true false true true\nfalse false false true 7 true 0\ntrue true true false true true\nfalse false false true 7 true 0\ntrue true true false true true\nfalse false false true 7 true 0\ntrue true true false true true\nfalse false false true 7 true 0\ntrue true true false true true\nfalse false false true 7 true 0\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void AbsentCallableDeletionSucceedsRegardlessOfIntegrity(
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
