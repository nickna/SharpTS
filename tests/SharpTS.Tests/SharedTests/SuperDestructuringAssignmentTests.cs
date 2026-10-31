using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class SuperDestructuringAssignmentTests
{
    [Theory, ModeData]
    public void ArrayAndObjectTargetsUseTheDerivedReceiver(ExecutionMode mode)
        => Assert.Equal("array 7\nobject 8\ndefault 9\n", TestHarness.Run("""
            class Base {
                label = 'base';
                set value(next: number) { console.log(this.label, next); }
            }
            class Child extends Base {
                label = 'array';
                write() {
                    [super.value] = [7];
                    this.label = 'object'; ({x: super.value} = {x: 8});
                    this.label = 'default'; [super.value = 9] = [];
                }
            }
            new Child().write();
            """, mode));

    [Theory, ModeData]
    public void DefaultInitializerRunsBeforeTheSuperReferenceIsResolved(ExecutionMode mode)
        => Assert.Equal("replacement 7\n", TestHarness.Run("""
            class Base { set value(next: number) { console.log('original', next); } }
            class Child extends Base { write() { [super.value = change()] = []; } }
            function change(): number {
                Object.setPrototypeOf(Child.prototype, {
                    set value(next: number) { console.log('replacement', next); }
                });
                return 7;
            }
            new Child().write();
            """, mode));

    [Theory, ModeData]
    public void ObjectSourceGetterRunsBeforeTheSuperReferenceIsResolved(ExecutionMode mode)
        => Assert.Equal("replacement 8\n", TestHarness.Run("""
            class Base { set value(next: number) { console.log('original', next); } }
            class Child extends Base {
                write(source: {x: number}) { ({x: super.value} = source); }
            }
            const source = { get x(): number {
                Object.setPrototypeOf(Child.prototype, {
                    set value(next: number) { console.log('replacement', next); }
                });
                return 8;
            } };
            new Child().write(source);
            """, mode));
}
