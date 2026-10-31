using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class SuperCompoundLogicalAssignmentTests
{
    [Theory, ModeData]
    public void StaticWritesUseTheActualReceiverAndLogicalShortCircuiting(ExecutionMode mode)
        => Assert.Equal("8 8 1\n7 7 0\n7 7 1\n7 7 null\n0 7 3\n", TestHarness.Run("""
            class Base { static value: any = 1; }
            class Child extends Base {
                static add() { return super.value += 7; }
                static or() { return super.value ||= 7; }
                static and() { return super.value &&= 7; }
                static nullish() { return super.value ??= 7; }
                static skip() { return super.value ||= (++calls); }
            }
            let calls = 0;
            console.log(Child.add(), Child.value, Base.value);
            Base.value = 0; console.log(Child.or(), Child.value, Base.value);
            Base.value = 1; console.log(Child.and(), Child.value, Base.value);
            Base.value = null; console.log(Child.nullish(), Child.value, Base.value);
            Base.value = 3; Child.skip(); console.log(calls, Child.value, Base.value);
            """, mode));

    [Theory, ModeData]
    public void GetterRunsOnceBeforeRhsAndSetterUsesCapturedBase(ExecutionMode mode)
        => Assert.Equal("get child\nrhs\nset child 7\n7\n", TestHarness.Run("""
            class Base {
                get value(): number { console.log('get', this.label); return 2; }
                set value(next: number) { console.log('set', this.label, next); }
                label = 'base';
            }
            class Child extends Base {
                label = 'child';
                write() { return super.value += change(); }
            }
            function change(): number {
                console.log('rhs');
                Object.setPrototypeOf(Child.prototype, { set value(next: number) { console.log('replacement'); } });
                return 5;
            }
            console.log(new Child().write());
            """, mode));

    [Theory, ModeData]
    public void AwaitedRhsRetainsTheSuperReference(ExecutionMode mode)
        => Assert.Equal("7 7 2\n", TestHarness.Run("""
            class Base { static value = 2; }
            class Child extends Base {
                static async write() { return super.value += await Promise.resolve(5); }
            }
            Child.write().then(value => console.log(value, Child.value, Base.value));
            """, mode));
}
