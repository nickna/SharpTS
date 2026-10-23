using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ClassAccessorLoopCaptureTests
{
    private static readonly string Source = """
        const states: any[] = [];
        for (const seed of [10, 20]) {
            let value = seed, reads = 0;
            class Owner {
                get slot() { reads++; return value; }
                set slot(next: number) { value = next; }
            }
            const prototype: any = Owner.prototype;
            Object.freeze(prototype);
            prototype.slot = seed + 1;
            console.log(value, prototype.slot, reads);
            states.push({ prototype, read: () => value, count: () => reads });
        }
        states[0].prototype.slot = 99;
        console.log(states[0].read(), states[1].read(), states[0].prototype.slot,
            states[1].prototype.slot, states[0].count(), states[1].count());
        """.ReplaceLineEndings("\n") + "\n";

    [Theory, ModeData]
    public void EscapingAccessorsShareTheirOwnIterationBindings(ExecutionMode mode)
    {
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(Source)).Diagnostics);
        Assert.Equal("11 11 1\n21 21 1\n99 21 99 21 2 2\n", TestHarness.Run(Source, mode));
    }

    [Theory, ModeData]
    public void NestedDefinitionsShareTheirEnclosingIterationBinding(ExecutionMode mode)
    {
        string source = """
            const states: any[] = [];
            for (const seed of [10, 20]) {
                let value = seed;
                class Outer {
                    make() {
                        return class Inner {
                            get slot() { return value; }
                            set slot(next: number) { value = next; }
                        };
                    }
                }
                const type: any = new Outer().make();
                Object.freeze(type.prototype);
                type.prototype.slot = seed + 1;
                states.push({ type, read: () => value });
                console.log(value, type.prototype.slot);
            }
            states[0].type.prototype.slot = 99;
            console.log(states[0].read(), states[1].read(), states[0].type.prototype.slot,
                states[1].type.prototype.slot);
            """.ReplaceLineEndings("\n") + "\n";
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
        Assert.Equal("11 11\n21 21\n99 21 99 21\n", TestHarness.Run(source, mode));
    }
}
