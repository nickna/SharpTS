using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ArrayInheritedAccessorPresenceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-strict-913-inherited-getterless-accessor", """
            const values: any[] = [];
            function write(target: any) { "use strict"; target[913] = 7; }
            try {
                Object.defineProperty(Array.prototype, "913", { get: undefined, configurable: true });
                try { write(values); console.log("accepted"); }
                catch (error) { console.log(error instanceof TypeError); }
                console.log(Object.hasOwn(values, "913"), values.length);
            } finally { delete (Array.prototype as any)["913"]; }
            """, "true\nfalse 0\n"];
        yield return ["nearest-getterless-mask-and-inherited-setter-original-receiver", """
            function sloppyWrite(target: any, value: number) { return target[913] = value; }
            function strictWrite(target: any, value: number) { 'use strict'; return target[913] = value; }
            const state = { calls: 0, stored: 0, receiver: false };
            const far: any = Object.create(Array.prototype);
            const nearest: any = Object.create(far);
            const values: any[] = [];
            Object.setPrototypeOf(values, nearest);
            Object.defineProperty(far, '913', {
                set(next: number) { state.calls++; state.stored = next; state.receiver = this === values; },
                configurable: true
            });
            Object.defineProperty(nearest, '913', { get: undefined, configurable: true });
            console.log(sloppyWrite(values, 7), state.calls, Object.hasOwn(values, '913'), values.length);
            try { strictWrite(values, 8); console.log(false); }
            catch (error) { console.log(error instanceof TypeError); }
            console.log(state.calls, Object.hasOwn(values, '913'), values.length);
            delete nearest['913'];
            console.log(strictWrite(values, 9), state.calls, state.stored, state.receiver, Object.hasOwn(values, '913'), values.length);
            Object.preventExtensions(values);
            console.log(strictWrite(values, 10), state.calls, state.stored, state.receiver,
                Object.hasOwn(values, '913'), values.length, Object.isExtensible(values));
            """, "7 0 false 0\ntrue\n0 false 0\n9 1 9 true false 0\n10 2 10 true false 0 false\n"];
        yield return ["own-index-and-writable-inherited-data-preserve-attributes", """
            const state = { inheritedSets: 0 };
            const own: any[] = [];
            try {
                Object.defineProperty(Array.prototype, '913', {
                    set(next: number) { state.inheritedSets++; }, configurable: true
                });
                Object.defineProperty(own, '913', { value: 1, writable: true, enumerable: false, configurable: false });
                console.log(own[913] = 7, own[913], state.inheritedSets, own.length);
                const descriptor = Object.getOwnPropertyDescriptor(own, '913')!;
                console.log(descriptor.value, descriptor.writable, descriptor.enumerable, descriptor.configurable);
            } finally { delete (Array.prototype as any)['913']; }
            const selected: any = Object.create(Array.prototype);
            Object.defineProperty(selected, '913', { value: 3, writable: true, enumerable: false, configurable: false });
            const values: any[] = [];
            Object.setPrototypeOf(values, selected);
            function strictWrite(target: any) { 'use strict'; return target[913] = 9; }
            console.log(strictWrite(values), values[913], selected['913'], values.length, Object.hasOwn(values, '913'));
            const created = Object.getOwnPropertyDescriptor(values, '913')!;
            console.log(created.value, created.writable, created.enumerable, created.configurable);
            """, "7 7 0 914\n7 true false false\n9 9 3 914 true\n9 true true true\n"];
        yield return ["sequential-zero-empty-hole-and-present-own-index-preservation", """
            function strictZero(target: any) { 'use strict'; return target[0] = 8; }
            const empty: any[] = [], hole: any[] = Array(1), present: any[] = [1];
            const sync = { emptyResult: 0, holeResult: 0, emptyThrows: false, holeThrows: false,
                emptyOwn: false, holeOwn: false, emptyLength: -1, holeLength: -1, ownValue: 0 };
            try {
                Object.defineProperty(Array.prototype, '0', { get: undefined, configurable: true });
                sync.emptyResult = empty[0] = 7;
                sync.holeResult = hole[0] = 7;
                try { strictZero(empty); } catch (error) { sync.emptyThrows = error instanceof TypeError; }
                try { strictZero(hole); } catch (error) { sync.holeThrows = error instanceof TypeError; }
                present[0] = 9;
                sync.ownValue = strictZero(present);
                sync.emptyOwn = Object.hasOwn(empty, '0'); sync.holeOwn = Object.hasOwn(hole, '0');
                sync.emptyLength = empty.length; sync.holeLength = hole.length;
            } finally { delete (Array.prototype as any)['0']; }
            console.log(sync.emptyResult, sync.holeResult, sync.emptyThrows, sync.holeThrows,
                sync.emptyOwn, sync.holeOwn, sync.emptyLength, sync.holeLength, sync.ownValue, present[0]);
            async function run() {
                await Promise.resolve(0);
                const emptyAsync: any[] = [], holeAsync: any[] = Array(1), presentAsync: any[] = [2];
                const result = { emptyResult: 0, holeResult: 0, emptyOwn: false, holeOwn: false,
                    emptyLength: -1, holeLength: -1, ownValue: 0 };
                try {
                    Object.defineProperty(Array.prototype, '0', { set: undefined, configurable: true });
                    result.emptyResult = emptyAsync[0] = 11;
                    result.holeResult = holeAsync[0] = 12;
                    result.ownValue = presentAsync[0] = 13;
                    result.emptyOwn = Object.hasOwn(emptyAsync, '0'); result.holeOwn = Object.hasOwn(holeAsync, '0');
                    result.emptyLength = emptyAsync.length; result.holeLength = holeAsync.length;
                } finally { delete (Array.prototype as any)['0']; }
                console.log(result.emptyResult, result.holeResult, result.emptyOwn, result.holeOwn,
                    result.emptyLength, result.holeLength, result.ownValue, presentAsync[0]);
            }
            run();
            """, "7 7 true true false false 0 1 8 8\n11 12 false false 0 1 13 13\n"];
        yield return ["sloppy-strict-inherited-accessor-presence-keeps-length", """
            function sloppyWrite(target: any) { return target[913] = 7; }
            function strictWrite(target: any) { 'use strict'; target[913] = 9; }
            const state = { gets: 0 };
            const forms: any[] = [
                { get: undefined, configurable: true },
                { set: undefined, configurable: true },
                { get: undefined, set: undefined, configurable: true },
                { get() { state.gets++; return 11; }, configurable: true }
            ];
            for (let kind = 0; kind < forms.length; kind++) {
                const values: any[] = [];
                try {
                    Object.defineProperty(Array.prototype, '913', forms[kind]);
                    const descriptor = Object.getOwnPropertyDescriptor(Array.prototype, '913')!;
                    console.log(kind, Object.hasOwn(descriptor, 'get'), Object.hasOwn(descriptor, 'set'),
                        Object.hasOwn(descriptor, 'value'), Object.hasOwn(descriptor, 'writable'));
                    console.log(sloppyWrite(values), Object.hasOwn(values, '913'), values.length, state.gets);
                    try { strictWrite(values); console.log(false); }
                    catch (error) { console.log(error instanceof TypeError); }
                    console.log(Object.hasOwn(values, '913'), values.length, state.gets);
                } finally { delete (Array.prototype as any)['913']; }
            }
            """, "0 true true false false\n7 false 0 0\ntrue\nfalse 0 0\n1 true true false false\n7 false 0 0\ntrue\nfalse 0 0\n2 true true false false\n7 false 0 0\ntrue\nfalse 0 0\n3 true true false false\n7 false 0 0\ntrue\nfalse 0 0\n"];
    }

    public static IEnumerable<object[]> CompiledCases() => Cases();

    public static IEnumerable<object[]> ModeCases() => Cases()
        .Select(row => new object[] { row[0], row[1], row[2], ExecutionMode.Interpreted })
        .Concat(CompiledCases().Select(row =>
            new object[] { row[0], row[1], row[2], ExecutionMode.Compiled }));

    [Theory, MemberData(nameof(ModeCases))]
    public void SimpleArrayWritesHonorAccessorPresenceAndNearestDescriptors(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        if (name == "own-index-and-writable-inherited-data-preserve-attributes")
        {
            // Match the historically strict-qualified full source, including its final LF.
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
