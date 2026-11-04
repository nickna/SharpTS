using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class P3DynamicClrFieldReadsTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (ExecutionMode mode in new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled })
        {
            yield return [mode, "boxed-value-storage", """
                @DotNetType("System.Numerics.Vector2")
                declare class Vector { constructor(x:number,y:number); x:number; y:number; }
                const value:Vector=new Vector(1,2);
                console.log(value.x,value.y);
                const values:any[]=[value];
                console.log(values[0].x,values[0].y);
                const record:any={value};
                console.log(record.value.x,record.value.y,typeof record.value.x);
                """, "1 2\n1 2\n1 2 number\n"];
            yield return [mode, "reference-field-spelling", """
                @DotNetType("System.Runtime.CompilerServices.StrongBox<System.String>")
                declare class Box { constructor(value:string); value:string; }
                const value:Box=new Box('field');
                const values:any[]=[value];
                console.log(value.value,values[0].value,values[0].Value,values[0].vAlUe);
                """, "field field field undefined\n"];
            yield return [mode, "null-field-value", """
                @DotNetType("System.Runtime.CompilerServices.StrongBox<System.String>")
                declare class Box { constructor(value:string); value:string|null; }
                const value:Box=new Box('before');
                value.value=null;
                const alias:any=value;
                console.log(alias.value,alias.Value,alias.missing);
                """, "null null undefined\n"];
            yield return [mode, "property-and-guest-name-controls", """
                @DotNetType("System.Version")
                declare class Version { constructor(major:number,minor:number); major:number; minor:number; }
                const value:any=new Version(3,4);
                console.log(value.major,value.minor,typeof value.major);
                class Guest { value=7; get label(){return 'getter';} }
                const guest:any=new Guest();
                console.log(guest.value,guest.Value,guest.label,guest.Label);
                """, "3 4 number\n7 undefined getter undefined\n"];
        }
    }

    [Theory, MemberData(nameof(Cases))]
    public void DynamicClrFieldsKeepInteropNamesAfterStorage(ExecutionMode mode, string name, string source, string expected)
    {
        string actual = TestHarness.Run(source, mode, DecoratorMode.Legacy);
        Assert.True(expected == actual, $"{name} ({mode}): expected '{expected}', actual '{actual}'.");
    }
}
