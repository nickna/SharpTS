using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ParameterPropertyDeclarationOrderTests
{
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
            """;
        Assert.Equal("initializer\ntrue true true\nvalue,earlier\n",
            TestHarness.RunUnchecked(source, ExecutionMode.Interpreted));
    }
}
