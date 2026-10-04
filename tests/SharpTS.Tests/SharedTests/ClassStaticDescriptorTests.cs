using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class ClassStaticDescriptorTests
{
    // Original #1861 regression, retained unchanged for #1900.
    [Theory, ModeData]
    public void NonWritableDescriptorWinsOverFieldStorage(ExecutionMode mode)
    {
        const string source = """
            class Box<T> { static count: number = 4; }
            const Alias: any = Box;
            Object.defineProperty(Box, "count", { value: 12, writable: false, configurable: true });
            Alias.count = 9;
            console.log(Alias.count);
            console.log(Alias === Box);
            """;
        Assert.Equal("12\ntrue\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StrictWritesRejectOwnAndInheritedNonWritableStatics(ExecutionMode mode)
    {
        const string source = """
            "use strict";
            class Base<T> { static count = 4; static writable = 5; }
            class Derived<T> extends Base<T> {}
            const base: any = Base;
            const derived: any = Derived;
            Object.defineProperty(Base, "count", { value: 4, writable: false });
            try { base.count = 9; } catch (e) { console.log(e instanceof TypeError); }
            try { derived["count"] = 10; } catch (e) { console.log(e instanceof TypeError); }
            derived.writable = 11;
            console.log(base.count, derived.count, base.writable, derived.writable);
            console.log(Object.getOwnPropertyDescriptor(Base, "count").writable);
            console.log(Object.hasOwn(Derived, "count"));
            """;
        Assert.Equal("true\ntrue\n4 4 5 11\nfalse\nfalse\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void StrictWriteRejectsOwnNonWritableStatic(ExecutionMode mode)
    {
        const string source = """
            "use strict";
            class Box<T> { static count = 4; }
            const box: any = Box;
            Object.defineProperty(Box, "count", { value: 4, writable: false });
            try { box.count = 9; } catch (e) { console.log(e instanceof TypeError); }
            console.log(box.count);
            """;
        Assert.Equal("true\n4\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ReadModifyWriteOperationsRespectNonWritableStatic(ExecutionMode mode)
    {
        const string source = """
            "use strict";
            class Box<T> { static count = 4; }
            const box: any = Box;
            Object.defineProperty(Box, "count", { value: 4, writable: false });
            try { box.count++; } catch (e) { console.log(e instanceof TypeError); }
            try { box.count += 2; } catch (e) { console.log(e instanceof TypeError); }
            try { box.count *= 2; } catch (e) { console.log(e instanceof TypeError); }
            try { box.count &&= 3; } catch (e) { console.log(e instanceof TypeError); }
            console.log(box.count, Object.getOwnPropertyDescriptor(Box, "count").value);
            """;
        Assert.Equal("true\ntrue\ntrue\ntrue\n4 4\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AttributeOnlyDefinitionPreservesStaticValue(ExecutionMode mode)
    {
        const string source = """
            class Box { static count = 4; }
            const box: any = Box;
            Object.defineProperty(Box, "count", { writable: false });
            box.count = 9;
            console.log(box.count, Object.getOwnPropertyDescriptor(Box, "count").value);
            """;
        Assert.Equal("4 4\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void RedefiningWritableDescriptorPreservesValueAndAllowsAssignment(ExecutionMode mode)
    {
        const string source = """
            class Box { static count = 4; }
            const box: any = Box;
            Object.defineProperty(Box, "count", { value: 4, writable: false, configurable: true });
            box["count"] = 9;
            console.log(box.count);
            Object.defineProperty(Box, "count", { writable: true });
            box.count = 12;
            console.log(box.count, Object.getOwnPropertyDescriptor(Box, "count").value);
            """;
        Assert.Equal("4\n12 12\n", TestHarness.Run(source, mode));
    }
}
