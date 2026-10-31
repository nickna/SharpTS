using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2InstanceElementInitializationOrderTests
{
    [Theory, ModeData]
    public void FieldsAndAutoAccessorsInitializeInDeclarationOrder(ExecutionMode mode)
    {
        const string source = """
            const events: string[] = [];
            class AccessorFirst {
                accessor a = (events.push("accessor"), 1);
                b = (events.push("field"), 2);
            }
            class FieldFirst {
                b = (events.push("field"), 2);
                accessor a = (events.push("accessor"), 1);
            }
            const first = new AccessorFirst();
            console.log(events.join(","), first.a, first.b);
            events.length = 0;
            const second = new FieldFirst();
            console.log(events.join(","), second.a, second.b);
            events.length = 0;
            class Child extends AccessorFirst {
                accessor c = (events.push("child-accessor"), 3);
                d = (events.push("child-field"), 4);
            }
            const child = new Child();
            console.log(events.join(","), child.c, child.d);
            """;
        Assert.Equal("accessor,field 1 2\nfield,accessor 1 2\naccessor,field,child-accessor,child-field 3 4\n", TestHarness.Run(source, mode));
    }
}
