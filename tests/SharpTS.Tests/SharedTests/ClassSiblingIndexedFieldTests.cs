using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassSiblingIndexedFieldTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["exact-sibling-indexed-field-annotation", """
            class C {
                other: string = "ok";
                value: C["other"] = "value";
            }
            console.log(new C().other, new C().value);
            """, "ok value\n"];
        yield return ["forward-sibling-chain-keeps-runtime-fields", """
            class Forward {
                first: Forward['middle'] = 'first';
                middle: Forward['last'] = 'middle';
                last: string = 'last';
            }
            const value = new Forward();
            const first: string = value.first;
            const middle: string = value.middle;
            console.log(first, middle, value.last);
            """, "first middle last\n"];
        yield return ["inherited-and-generic-sibling-annotations", """
            class Parent { other: number = 3; }
            class Child extends Parent { value: Child['other'] = 4; }
            class Box<T> {
                other: T;
                value: Box<T>['other'];
                constructor(value: T) { this.other = value; this.value = value; }
            }
            const child = new Child();
            const numeric = new Box<number>(5);
            const text = new Box<string>('text');
            console.log(child.other, child.value, numeric.other, numeric.value, text.other, text.value);
            """, "3 4 5 5 text text\n"];
        yield return ["nested-annotation-and-static-instance-ownership", """
            class Wrapped {
                other: string = 'other';
                values: Wrapped['other'][] = ['first', 'second'];
                read: () => Wrapped['other'] = () => 'read';
                static other: Wrapped['other'] = 'static';
            }
            const value = new Wrapped();
            console.log(value.other, value.values.join(','), value.read(), Wrapped.other);
            """, "other first,second read static\n"];
        yield return ["inherited-only-sibling-indexed-field", """
            class Base { other: number = 3; }
            class Child extends Base { value: Child['other'] = 4; }
            const child = new Child();
            console.log(child.other, child.value);
            """, "3 4\n"];
        yield return ["forward-optional-sibling-and-transitive-read", """
            class ForwardOptional {
                value: ForwardOptional['other'] = undefined;
                copy: ForwardOptional['value'] = undefined;
                other?: string;
            }
            const value = new ForwardOptional();
            console.log(value.value === undefined, value.copy === undefined);
            """, "true true\n"];
    }

    public static IEnumerable<object[]> EligibleCases() => Cases();

    public static IEnumerable<object[]> ModeCases() => EligibleCases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void SiblingFieldAnnotationsPassStrictCheckingBeforeExecution(
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
