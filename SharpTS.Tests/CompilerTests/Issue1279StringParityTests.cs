using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public class Issue1279StringParityTests
{
    [Fact]
    public void String_ValueOf_Rejects_Every_Non_String_Receiver()
    {
        const string source = """
            const valueOf: any = String.prototype.valueOf;
            function rejects(label: string, invoke: any) {
              try {
                invoke();
                console.log(label + ":no");
              } catch (error) {
                console.log(label + ":" + (error instanceof TypeError));
              }
            }
            rejects("boolean", () => valueOf.call(true));
            rejects("number", () => valueOf.call(-0));
            rejects("null", () => valueOf.call(null));
            rejects("undefined", () => valueOf.call());
            rejects("symbol", () => valueOf.call(Symbol("desc")));
            rejects("object", () => valueOf.call({ toString() { return "str"; } }));
            rejects("array", () => valueOf.call(["s", "t", "r"]));
            rejects("coercion", () => "str" + { valueOf });
            """;

        Assert.Equal(
            "boolean:true\nnumber:true\nnull:true\nundefined:true\nsymbol:true\n" +
            "object:true\narray:true\ncoercion:true\n",
            TestHarness.RunCompiled(source));
    }

    [Fact]
    public void String_Prototype_Constructor_Uses_Js_Boxing_Semantics()
    {
        const string source = """
            const Constructor: any = String.prototype.constructor;
            const boxed: any = new Constructor("choosing one");
            console.log(boxed.valueOf());
            console.log(Object.prototype.toString.call(boxed));
            """;

        Assert.Equal("choosing one\n[object String]\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void String_Raw_Has_Standard_Builtin_Descriptor()
    {
        const string source = """
            const descriptor: any = Object.getOwnPropertyDescriptor(String, "raw");
            console.log(descriptor.value === String.raw);
            console.log(descriptor.writable);
            console.log(descriptor.enumerable);
            console.log(descriptor.configurable);
            """;

        Assert.Equal("true\ntrue\nfalse\ntrue\n", TestHarness.RunCompiled(source));
    }

    [Fact]
    public void IndexOf_Coerces_Position_Through_SymbolToPrimitive()
    {
        const string source = """
            const position: any = {
              [Symbol.toPrimitive]: function(hint: any) {
                console.log(hint);
                return 1;
              },
              valueOf: function() { throw "valueOf"; },
              toString: function() { throw "toString"; }
            };
            console.log("aaaa".indexOf("aa", position));
            """;

        Assert.Equal("number\n1\n", TestHarness.RunCompiled(source));
    }
}
