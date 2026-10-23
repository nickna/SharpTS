using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class PlainStringClassFieldGuestValueTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-plain-string-field-any-undefined-constructor", """
            class Field {
                value: string;
                constructor(value: any) { this.value = value; }
            }
            console.log(new Field(undefined).value === undefined);
            """, "true\n"];
        yield return ["raw-any-constructor-and-string-return-preserve-identity", """
            const state = { hooks: 0 };
            const raw: any = { toString() { state.hooks++; throw 'converted'; } };
            class Field {
                value: string;
                constructor(value: any) { this.value = value; }
                read(): string { return this.value; }
            }
            const inputs: any[] = [undefined, null, 11, false, raw, 'text'];
            for (let index = 0; index < inputs.length; index++) {
                const input: any = inputs[index];
                try {
                    const field = new Field(input);
                    console.log(index, Object.is(field.value, input), Object.is((field as any).value, input), Object.is(field.read(), input), Object.is(Reflect.get(field, 'value'), input), Object.is(Object.getOwnPropertyDescriptor(field, 'value')!.value, input));
                } catch (error) {
                    console.log(index, 'rejected');
                }
            }
            console.log(state.hooks);
            """, "0 true true true true true\n1 true true true true true\n2 true true true true true\n3 true true true true true\n4 true true true true true\n5 true true true true true\n0\n"];
        yield return ["ordinary-setter-and-dynamic-field-writes-preserve-identity", """
            const state = { hooks: 0 };
            const raw: any = { toString() { state.hooks++; throw 'converted'; } };
            class Field {
                value: string = 'initial';
                replace(value: any): any { return this.value = value; }
                read(): string { return this.value; }
            }
            const inputs: any[] = [undefined, null, 11, false, raw, 'text'];
            for (let index = 0; index < inputs.length; index++) {
                const input: any = inputs[index];
                try {
                    const field = new Field();
                    const assigned = field.replace(input);
                    const first = Object.is(assigned, input) && Object.is(field.read(), input);
                    const dynamic: any = field;
                    dynamic.value = input;
                    const second = Object.is(field.value, input) && Object.is(field.read(), input);
                    field.value = input;
                    console.log(index, first, second, Object.is(field.value, input), Object.is(field.read(), input));
                } catch (error) {
                    console.log(index, 'rejected');
                }
            }
            const arithmetic = new Field();
            arithmetic.value = 11 as any;
            console.log(arithmetic.value + 1);
            arithmetic.value = undefined as any;
            console.log(Number.isNaN((arithmetic.value + 1) as any));
            console.log(state.hooks);
            """, "0 true true true true\n1 true true true true\n2 true true true true\n3 true true true true\n4 true true true true\n5 true true true true\n12\ntrue\n0\n"];
        yield return ["class-expression-generic-and-typed-string-controls", """
            const state = { hooks: 0 };
            const raw: any = { toString() { state.hooks++; throw 'converted'; } };
            const Expression = class {
                value: string;
                constructor(value: any) { this.value = value; }
                read(): string { return this.value; }
            };
            class Generic<T> {
                value: string;
                tag: T;
                constructor(value: any, tag: T) { this.value = value; this.tag = tag; }
                read(): string { return this.value; }
            }
            class Child extends Generic<number> {}
            for (const input of [undefined, raw, 'text'] as any[]) {
                try {
                    const expression = new Expression(input);
                    const generic = new Generic<number>(input, 3);
                    const child = new Child(input, 4);
                    console.log(Object.is(expression.value, input), Object.is(expression.read(), input), Object.is(generic.value, input), Object.is(generic.read(), input), generic.tag, Object.is(child.value, input), Object.is(child.read(), input), child.tag);
                } catch (error) {
                    console.log('rejected');
                }
            }
            class Typed {
                value: string;
                constructor(value: string = 'fallback') { this.value = value; }
                replace(value: string): string { return this.value = value; }
                read(): string { return this.value; }
            }
            const typed = new Typed('before');
            console.log(typed.read(), typed.replace('after'), typed.read(), new Typed().read(), state.hooks);
            """, "true true true true 3 true true 4\ntrue true true true 3 true true 4\ntrue true true true 3 true true 4\nbefore after after fallback 0\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void PlainStringFieldsPreserveGuestValuesAdmittedThroughAny(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source));
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else Assert.Equal(expected, TestHarness.RunInterpreted(source));
    }
}
