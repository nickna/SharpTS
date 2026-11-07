using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class ArrayLexicalSuperTests
{
    [Fact]
    public void ArrayConstructorBindsItsExoticReceiverBeforeTheArrowResumes()
    {
        const string source = """
            const C = class extends Array {
                value = 7;
                constructor() {
                    const initialize = () => {
                        const receiver = super(3);
                        console.log(receiver === this, this.value, this.length, 0 in this);
                    };
                    initialize();
                }
            };
            const value = new C();
            value[1] = 9;
            console.log(value.value, Array.isArray(value), value instanceof C,
                value instanceof Array, value.length, value[1]);
            """;

        Assert.Equal("true 7 3 false\n7 true true true 3 9\n", TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }

    [Fact]
    public void RepeatedSuperConstructsBeforeRejectingTheSecondBinding()
    {
        const string source = """
            let argumentsSeen = 0, fieldsInitialized = 0;
            const C = class extends Array {
                value = ++fieldsInitialized;
                constructor(lexical) {
                    const initialize = (length) => super((argumentsSeen++, length));
                    if (lexical) initialize(2);
                    else super(2);
                    try { initialize(-1); } catch (error) { console.log(error instanceof RangeError); }
                    try { initialize(5); } catch (error) { console.log(error instanceof ReferenceError); }
                }
            };
            const first = new C(true), second = new C(false);
            console.log(first.length, second.length, first.value, second.value, argumentsSeen, fieldsInitialized);
            """;

        Assert.Equal("true\ntrue\ntrue\ntrue\n2 2 1 2 5 2\n", TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }

    [Fact]
    public void ClassDeclarationBindsAnArrayWithTheOriginalNewTarget()
    {
        const string source = """
            class C extends Array {
                value = 7;
                constructor() {
                    const initialize = () => super('first', 'second');
                    const receiver = initialize();
                    console.log(receiver === this, new.target === C);
                }
            }
            const value = new C();
            console.log(Array.isArray(value), value instanceof C, value.value, value.join(','));
            """;

        Assert.Equal("true true\ntrue true 7 first,second\n", TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }

    [Fact]
    public void DirectSuperKeepsTheReceiverBoundWhileItsArgumentsWereEvaluated()
    {
        const string source = """
            let runs = 0;
            const C = class extends Array {
                value = ++runs;
                constructor() {
                    let inner;
                    const initialize = () => inner = super(2);
                    try { super(initialize()); }
                    catch (error) { console.log(error instanceof ReferenceError); }
                    console.log(this === inner);
                }
            };
            const value = new C();
            console.log(value.length, value.value, runs, Array.isArray(value));
            """;

        Assert.Equal("true\ntrue\n2 1 1 true\n", TestHarness.RunUnchecked(source, ExecutionMode.Compiled));
    }
}
