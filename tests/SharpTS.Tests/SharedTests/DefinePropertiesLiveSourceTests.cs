using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class DefinePropertiesLiveSourceTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["array-hidden-key-reveal-keeps-own-snapshot-and-delayed-application", """
            const descriptors: any = [];
            const firstKey = '0', secondKey = '1', addedKey = '2';
            const state = { trace: '', sourceReceiver: true };
            const firstDescriptor: any = {}, secondDescriptor: any = {}, target: any = {};
            Object.defineProperty(target, firstKey, { value: 0, writable: true, enumerable: true, configurable: true });
            Object.defineProperty(firstDescriptor, 'value', { get() {
                state.trace += 'convert-first,';
                Object.defineProperty(descriptors, secondKey, { enumerable: true });
                Object.defineProperty(descriptors, addedKey, { value: { value: 3 }, enumerable: true, configurable: true });
                return 1;
            } });
            Object.defineProperty(secondDescriptor, 'value', { get() { state.trace += 'convert-second:' + target[firstKey] + ','; return 2; } });
            Object.defineProperty(descriptors, firstKey, { enumerable: true, configurable: true, get: function(this: any) {
                state.trace += 'get-first,'; state.sourceReceiver = state.sourceReceiver && this === descriptors; return firstDescriptor;
            } });
            Object.defineProperty(descriptors, secondKey, { enumerable: false, configurable: true, get: function(this: any) {
                state.trace += 'get-second:' + target[firstKey] + ',';
                state.sourceReceiver = state.sourceReceiver && this === descriptors; return secondDescriptor;
            } });
            Object.defineProperties(target, descriptors);
            console.log(state.trace, target[firstKey], target[secondKey], Object.hasOwn(target, addedKey), state.sourceReceiver);
            const first = Object.getOwnPropertyDescriptor(target, firstKey)!, second = Object.getOwnPropertyDescriptor(target, secondKey)!;
            console.log(first.writable, first.enumerable, first.configurable, second.writable, second.enumerable, second.configurable);
            """, "get-first,convert-first,get-second:0,convert-second:0, 1 2 false true\ntrue true true false false false\n"];
        yield return ["array-hide-and-delete-later-source-key", """
            for (const action of ['hide', 'delete']) {
                const descriptors: any = [];
                const firstKey = '0', secondKey = '1';
                const state = { trace: '', threw: false, sourceReceiver: true };
                const firstDescriptor: any = {}, secondDescriptor: any = {}, target: any = {};
                Object.defineProperty(firstDescriptor, 'value', { get() {
                    state.trace += 'convert-first,';
                    if (action === 'hide') Object.defineProperty(descriptors, secondKey, { enumerable: false });
                    else delete descriptors[secondKey];
                    return 1;
                } });
                Object.defineProperty(secondDescriptor, 'value', { get() { state.trace += 'convert-second,'; return 2; } });
                Object.defineProperty(descriptors, firstKey, { enumerable: true, configurable: true, get: function(this: any) {
                    state.trace += 'get-first,'; state.sourceReceiver = state.sourceReceiver && this === descriptors; return firstDescriptor;
                } });
                Object.defineProperty(descriptors, secondKey, { enumerable: true, configurable: true, get: function(this: any) {
                    state.trace += 'get-second,'; state.sourceReceiver = state.sourceReceiver && this === descriptors; return secondDescriptor;
                } });
                try { Object.defineProperties(target, descriptors); }
                catch (error) { state.threw = error instanceof TypeError; }
                console.log(action, state.trace, state.threw, Object.hasOwn(target, firstKey), Object.hasOwn(target, secondKey), state.sourceReceiver);
            }
            """, "hide get-first,convert-first, false true false true\ndelete get-first,convert-first, false true false true\n"];
        yield return ["exact-delete-later-ordinary-source-key", """
            const trace: string[] = [];
            const descriptors: any = {}, firstDescriptor: any = {}, target: any = {};
            Object.defineProperty(firstDescriptor, "value", {
                get() {
                    trace.push("convert-first");
                    delete descriptors.second;
                    return 1;
                }
            });
            Object.defineProperty(descriptors, "first", {
                enumerable: true,
                get() { trace.push("get-first"); return firstDescriptor; }
            });
            Object.defineProperty(descriptors, "second", {
                enumerable: true, configurable: true,
                get() { trace.push("get-second"); return { value: 2 }; }
            });
            let threw = false;
            try { Object.defineProperties(target, descriptors); }
            catch (error) { threw = error instanceof TypeError; }
            console.log(trace.join(","), threw, Object.hasOwn(target, "first"), Object.hasOwn(target, "second"));
            """, "get-first,convert-first false true false\n"];
        yield return ["exact-hide-later-ordinary-source-key", """
            const trace: string[] = [];
            const descriptors: any = {}, firstDescriptor: any = {}, target: any = {};
            Object.defineProperty(firstDescriptor, "value", {
                get() {
                    trace.push("convert-first");
                    Object.defineProperty(descriptors, "second", { enumerable: false });
                    return 1;
                }
            });
            Object.defineProperty(descriptors, "first", {
                enumerable: true,
                get() { trace.push("get-first"); return firstDescriptor; }
            });
            Object.defineProperty(descriptors, "second", {
                enumerable: true, configurable: true,
                get() { trace.push("get-second"); return { value: 2 }; }
            });
            let threw = false;
            try { Object.defineProperties(target, descriptors); }
            catch (error) { threw = error instanceof TypeError; }
            console.log(trace.join(","), threw, Object.hasOwn(target, "first"), Object.hasOwn(target, "second"));
            """, "get-first,convert-first false true false\n"];
        yield return ["function-hidden-key-reveal-keeps-own-snapshot-and-delayed-application", """
            const descriptors: any = function map() {};
            const firstKey = 'first', secondKey = 'second', addedKey = 'added';
            const state = { trace: '', sourceReceiver: true };
            const firstDescriptor: any = {}, secondDescriptor: any = {}, target: any = {};
            Object.defineProperty(target, firstKey, { value: 0, writable: true, enumerable: true, configurable: true });
            Object.defineProperty(firstDescriptor, 'value', { get() {
                state.trace += 'convert-first,';
                Object.defineProperty(descriptors, secondKey, { enumerable: true });
                Object.defineProperty(descriptors, addedKey, { value: { value: 3 }, enumerable: true, configurable: true });
                return 1;
            } });
            Object.defineProperty(secondDescriptor, 'value', { get() { state.trace += 'convert-second:' + target[firstKey] + ','; return 2; } });
            Object.defineProperty(descriptors, firstKey, { enumerable: true, configurable: true, get: function(this: any) {
                state.trace += 'get-first,'; state.sourceReceiver = state.sourceReceiver && this === descriptors; return firstDescriptor;
            } });
            Object.defineProperty(descriptors, secondKey, { enumerable: false, configurable: true, get: function(this: any) {
                state.trace += 'get-second:' + target[firstKey] + ',';
                state.sourceReceiver = state.sourceReceiver && this === descriptors; return secondDescriptor;
            } });
            Object.defineProperties(target, descriptors);
            console.log(state.trace, target[firstKey], target[secondKey], Object.hasOwn(target, addedKey), state.sourceReceiver);
            const first = Object.getOwnPropertyDescriptor(target, firstKey)!, second = Object.getOwnPropertyDescriptor(target, secondKey)!;
            console.log(first.writable, first.enumerable, first.configurable, second.writable, second.enumerable, second.configurable);
            """, "get-first,convert-first,get-second:0,convert-second:0, 1 2 false true\ntrue true true false false false\n"];
        yield return ["function-hide-and-delete-later-source-key", """
            for (const action of ['hide', 'delete']) {
                const descriptors: any = function map() {};
                const firstKey = 'first', secondKey = 'second';
                const state = { trace: '', threw: false, sourceReceiver: true };
                const firstDescriptor: any = {}, secondDescriptor: any = {}, target: any = {};
                Object.defineProperty(firstDescriptor, 'value', { get() {
                    state.trace += 'convert-first,';
                    if (action === 'hide') Object.defineProperty(descriptors, secondKey, { enumerable: false });
                    else delete descriptors[secondKey];
                    return 1;
                } });
                Object.defineProperty(secondDescriptor, 'value', { get() { state.trace += 'convert-second,'; return 2; } });
                Object.defineProperty(descriptors, firstKey, { enumerable: true, configurable: true, get: function(this: any) {
                    state.trace += 'get-first,'; state.sourceReceiver = state.sourceReceiver && this === descriptors; return firstDescriptor;
                } });
                Object.defineProperty(descriptors, secondKey, { enumerable: true, configurable: true, get: function(this: any) {
                    state.trace += 'get-second,'; state.sourceReceiver = state.sourceReceiver && this === descriptors; return secondDescriptor;
                } });
                try { Object.defineProperties(target, descriptors); }
                catch (error) { state.threw = error instanceof TypeError; }
                console.log(action, state.trace, state.threw, Object.hasOwn(target, firstKey), Object.hasOwn(target, secondKey), state.sourceReceiver);
            }
            """, "hide get-first,convert-first, false true false true\ndelete get-first,convert-first, false true false true\n"];
        yield return ["ordinary-hidden-key-reveal-keeps-own-snapshot-and-delayed-application", """
            const descriptors: any = {};
            const firstKey = 'first', secondKey = 'second', addedKey = 'added';
            const state = { trace: '', sourceReceiver: true };
            const firstDescriptor: any = {}, secondDescriptor: any = {}, target: any = {};
            Object.defineProperty(target, firstKey, { value: 0, writable: true, enumerable: true, configurable: true });
            Object.defineProperty(firstDescriptor, 'value', { get() {
                state.trace += 'convert-first,';
                Object.defineProperty(descriptors, secondKey, { enumerable: true });
                Object.defineProperty(descriptors, addedKey, { value: { value: 3 }, enumerable: true, configurable: true });
                return 1;
            } });
            Object.defineProperty(secondDescriptor, 'value', { get() { state.trace += 'convert-second:' + target[firstKey] + ','; return 2; } });
            Object.defineProperty(descriptors, firstKey, { enumerable: true, configurable: true, get: function(this: any) {
                state.trace += 'get-first,'; state.sourceReceiver = state.sourceReceiver && this === descriptors; return firstDescriptor;
            } });
            Object.defineProperty(descriptors, secondKey, { enumerable: false, configurable: true, get: function(this: any) {
                state.trace += 'get-second:' + target[firstKey] + ',';
                state.sourceReceiver = state.sourceReceiver && this === descriptors; return secondDescriptor;
            } });
            Object.defineProperties(target, descriptors);
            console.log(state.trace, target[firstKey], target[secondKey], Object.hasOwn(target, addedKey), state.sourceReceiver);
            const first = Object.getOwnPropertyDescriptor(target, firstKey)!, second = Object.getOwnPropertyDescriptor(target, secondKey)!;
            console.log(first.writable, first.enumerable, first.configurable, second.writable, second.enumerable, second.configurable);
            """, "get-first,convert-first,get-second:0,convert-second:0, 1 2 false true\ntrue true true false false false\n"];
        yield return ["native-carrier-fresh-hide-reveal-preservation", """
            for (const kind of ['Math', 'JSON', 'Date', 'Error']) {
                for (const phase of ['hide', 'reveal']) {
                    const source: any = kind === 'Math' ? Math : kind === 'JSON' ? JSON
                        : kind === 'Date' ? new Date(0) : new Error('probe');
                    const state = { trace: '', receivers: true, threw: false };
                    const target: any = {}, firstDescriptor: any = {}, secondDescriptor: any = {};
                    Object.defineProperty(target, '__dpFirst', {
                        value: 0, writable: true, enumerable: false, configurable: true
                    });
                    try {
                        Object.defineProperty(firstDescriptor, 'value', {
                            get() {
                                state.trace += 'convert-first,';
                                if (phase === 'hide') {
                                    Object.defineProperty(source, '__dpSecond', { enumerable: false });

                                } else {
                                    Object.defineProperty(source, '__dpSecond', { enumerable: true });
                                    Object.defineProperty(source, '__dpAdded', {
                                        value: { value: 3 }, enumerable: true, configurable: true
                                    });
                                }
                                return 1;
                            }
                        });
                        Object.defineProperty(secondDescriptor, 'value', {
                            get() { state.trace += 'convert-second:' + target.__dpFirst + ','; return 2; }
                        });
                        Object.defineProperty(source, '__dpFirst', {
                            enumerable: true, configurable: true,
                            get: function(this: any) {
                                state.receivers = state.receivers && this === source;
                                state.trace += 'get-first,';
                                return firstDescriptor;
                            }
                        });
                        Object.defineProperty(source, '__dpSecond', {
                            enumerable: phase !== 'reveal', configurable: true,
                            get: function(this: any) {
                                state.receivers = state.receivers && this === source;
                                state.trace += 'get-second:' + target.__dpFirst + ',';
                                return secondDescriptor;
                            }
                        });
                        try { Object.defineProperties(target, source); }
                        catch (error) { state.threw = error instanceof TypeError; }
                        console.log(kind, phase, state.trace, state.threw, target.__dpFirst,
                            Object.hasOwn(target, '__dpSecond'), Object.hasOwn(target, '__dpAdded'), state.receivers);
                        const first = Object.getOwnPropertyDescriptor(target, '__dpFirst')!;
                        const second = Object.getOwnPropertyDescriptor(target, '__dpSecond');
                        const secondValid = phase === 'reveal'
                            ? second !== undefined && second.value === 2 && second.writable === false
                                && second.enumerable === false && second.configurable === false
                            : second === undefined;
                        console.log(first.writable, first.enumerable, first.configurable, secondValid);
                    } finally {
                        delete source.__dpFirst;
                        delete source.__dpSecond;
                        delete source.__dpAdded;
                    }
                }
            }
            """, "Math hide get-first,convert-first, false 1 false false true\ntrue false true true\nMath reveal get-first,convert-first,get-second:0,convert-second:0, false 1 true false true\ntrue false true true\nJSON hide get-first,convert-first, false 1 false false true\ntrue false true true\nJSON reveal get-first,convert-first,get-second:0,convert-second:0, false 1 true false true\ntrue false true true\nDate hide get-first,convert-first, false 1 false false true\ntrue false true true\nDate reveal get-first,convert-first,get-second:0,convert-second:0, false 1 true false true\ntrue false true true\nError hide get-first,convert-first, false 1 false false true\ntrue false true true\nError reveal get-first,convert-first,get-second:0,convert-second:0, false 1 true false true\ntrue false true true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void SourceKeysRetainTheirSnapshotAndUseLiveDescriptorsBeforeGet(
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
