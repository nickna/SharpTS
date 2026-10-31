using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class SwitchLiteralFlowRuntimeTests
{
    [Theory, ModeData]
    public void TypedCaseBindingsRetainRuntimeSelectionAndFallthrough(ExecutionMode mode)
        => Assert.Equal("zero\none-or-two\nrest 2\nchanged c\nchanged b\nrest c\n", TestHarness.Run("""
            let choice: 1 | 2 = <1 | 2>1;
            function read(x: 0 | 1 | 2): void {
                switch (x) {
                    case 0: const zero: 0 = x; console.log('zero'); break;
                    case choice: const selected: 1 | 2 = x; console.log('one-or-two'); break;
                    default: const rest: 1 | 2 = x; console.log('rest', rest);
                }
            }
            function write(x: 'a' | 'b' | 'c'): void {
                switch (x) {
                    case 'a': x = 'c';
                    case 'b': const changed: 'b' | 'c' = x; console.log('changed', changed); break;
                    default: const rest: 'c' = x; console.log('rest', rest);
                }
            }
            read(0); read(1); read(2); write('a'); write('b'); write('c');
            """, mode));

    [Theory, ModeData]
    public void CaseWritesChangeTheLiveBindingAfterTheSubjectWasCaptured(ExecutionMode mode)
        => Assert.Equal("case b\ndefault a\ncase-first b\ndefault-first a\n", TestHarness.Run("""
            function read(x: 'a' | 'b', replacement: 'a' | 'b'): void {
                switch (x) {
                    case (x = replacement, 'a'): console.log('case', x); break;
                    default: console.log('default', x);
                }
            }
            function first(x: 'a' | 'b', replacement: 'a' | 'b'): void {
                switch (x) {
                    default: console.log('default-first', x); break;
                    case (x = replacement, 'a'): console.log('case-first', x); break;
                }
            }
            read('a', 'b'); read('b', 'a'); first('a', 'b'); first('b', 'a');
            """, mode));
}
