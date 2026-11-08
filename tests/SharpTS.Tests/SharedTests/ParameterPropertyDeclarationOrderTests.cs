using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ParameterPropertyDeclarationOrderTests
{
    [Fact]
    public void AConstructorBetweenFieldsDoesNotResetEarlierFieldWrites()
    {
        // The compiler's separate synthesized field reset is tracked in #3521.
        const string source = """
            class Item {
              first = (this.value = 7);
              constructor(public value = 1) {}
              last = this.value;
            }
            const item = new Item();
            console.log(item.first, item.last, item.value, Object.keys(item).join(','));
            """;
        Assert.Equal("7 7 1 value,first,last\n", TestHarness.RunUnchecked(source, ExecutionMode.Interpreted));
    }

    [Fact]
    public void ParameterPropertiesExistBeforeOrdinaryFieldsAndUseTheCurrentBinding()
    {
        const string source = """
            class Base {
              seen = Object.hasOwn(this, 'base') && this.base === undefined;
              constructor(public base = 2) {}
            }
            const Child = class extends Base {
              sawChild = Object.hasOwn(this, 'child') && this.child === undefined;
              constructor(public child = 1) {
                child++;
                (() => super())();
              }
            };
            const value = new Child();
            const descriptor = Object.getOwnPropertyDescriptor(value, 'child');
            console.log(value.seen, value.sawChild, value.base, value.child);
            console.log(Object.keys(value).join(','));
            console.log(descriptor.writable, descriptor.enumerable, descriptor.configurable);
            """;
        Assert.Equal("true true 2 2\nbase,seen,child,sawChild\ntrue true true\n",
            TestHarness.RunUnchecked(source, ExecutionMode.Interpreted));
    }

    [Fact]
    public void AbruptInitializerLeavesTheDeclarationOnAnEscapedReplacementReceiver()
    {
        // Compiled replacement receivers have a separate declaration gap (#3514).
        const string source = """
            const receiver = Object.create({ set value(input) { throw 'setter'; } });
            let escaped;
            class Base { constructor() { return receiver; } }
            class Child extends Base {
              earlier = 5;
              later = (() => { escaped = this; throw 'initializer'; })();
              constructor(public value = 1) { (() => super())(); }
            }
            try { new Child(); } catch (error) { console.log(error); }
            console.log(escaped === receiver, Object.hasOwn(escaped, 'value'), escaped.value === undefined);
            console.log(Object.keys(escaped).join(','));
            const descriptor = Object.getOwnPropertyDescriptor(escaped, 'value');
            console.log(descriptor.writable, descriptor.configurable);
            """;
        Assert.Equal("initializer\ntrue true true\nvalue,earlier\ntrue true\n",
            TestHarness.RunUnchecked(source, ExecutionMode.Interpreted));
    }
}
