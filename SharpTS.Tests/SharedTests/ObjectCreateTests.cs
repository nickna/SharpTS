using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

/// <summary>
/// Tests for Object.create() which creates a new object with the specified prototype.
/// Runs against both interpreter and compiler.
/// </summary>
public class ObjectCreateTests
{
    // Basic Object.create with null prototype
    [Theory, ModeData]
    public void Object_Create_NullPrototype_ReturnsEmptyObject(ExecutionMode mode)
    {
        var source = """
            let obj = Object.create(null);
            console.log(typeof obj);
            console.log(Object.keys(obj).length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\n0\n", output);
    }

    // Object.create with object prototype copies properties
    [Theory, ModeData]
    public void Object_Create_WithPrototype_CopiesProperties(ExecutionMode mode)
    {
        var source = """
            let proto = { x: 10, y: 20 };
            let obj = Object.create(proto);
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n", output);
    }

    // Object.create with prototype - new object is independent
    [Theory, ModeData]
    public void Object_Create_NewObjectIsIndependent(ExecutionMode mode)
    {
        var source = """
            let proto: { x: number, y?: number } = { x: 10 };
            let obj = Object.create(proto);
            obj.x = 100;
            obj.y = 200;
            console.log(proto.x);
            console.log(proto.y === undefined || proto.y === null);
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\ntrue\n100\n200\n", output);
    }

    // Object.create with properties object (second argument)
    [Theory, ModeData]
    public void Object_Create_WithPropertiesObject(ExecutionMode mode)
    {
        var source = """
            let obj = Object.create(null, {
                x: { value: 42, writable: true, enumerable: true, configurable: true },
                y: { value: 100, writable: true, enumerable: true, configurable: true }
            });
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n100\n", output);
    }

    // Object.create with both prototype and properties
    [Theory, ModeData]
    public void Object_Create_WithPrototypeAndProperties(ExecutionMode mode)
    {
        var source = """
            let proto = { a: 1 };
            let obj = Object.create(proto, {
                b: { value: 2, writable: true, enumerable: true, configurable: true }
            });
            console.log(obj.a);
            console.log(obj.b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n2\n", output);
    }

    // Object.create with non-writable property
    [Theory, ModeData]
    public void Object_Create_NonWritableProperty(ExecutionMode mode)
    {
        var source = """
            let obj = Object.create(null, {
                readonly: { value: 42, writable: false, enumerable: true, configurable: true }
            });
            console.log(obj.readonly);
            obj.readonly = 100;
            console.log(obj.readonly);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n42\n", output);
    }

    // Object.create with accessor property (getter)
    [Theory, ModeData]
    public void Object_Create_WithGetter(ExecutionMode mode)
    {
        var source = """
            let obj = Object.create(null, {
                _value: { value: 10, writable: true, enumerable: true, configurable: true },
                computed: {
                    get: function() { return this._value * 2; },
                    enumerable: true,
                    configurable: true
                }
            });
            console.log(obj.computed);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("20\n", output);
    }

    // Object.create with accessor property (getter and setter)
    [Theory, ModeData]
    public void Object_Create_WithGetterAndSetter(ExecutionMode mode)
    {
        var source = """
            let obj = Object.create(null, {
                _value: { value: 10, writable: true, enumerable: true, configurable: true },
                value: {
                    get: function() { return this._value; },
                    set: function(v: number) { this._value = v; },
                    enumerable: true,
                    configurable: true
                }
            });
            console.log(obj.value);
            obj.value = 50;
            console.log(obj.value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n50\n", output);
    }

    // Object.create returns object type
    [Theory, ModeData]
    public void Object_Create_ReturnsObjectType(ExecutionMode mode)
    {
        var source = """
            let obj = Object.create({ x: 1 });
            console.log(typeof obj);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\n", output);
    }

    // Object.create with empty prototype
    [Theory, ModeData]
    public void Object_Create_WithEmptyPrototype(ExecutionMode mode)
    {
        var source = """
            let proto: {} = {};
            let obj = Object.create(proto);
            console.log(typeof obj);
            console.log(Object.keys(obj).length);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("object\n0\n", output);
    }

    // Object.create with nested object in prototype
    [Theory, ModeData]
    public void Object_Create_WithNestedPrototype(ExecutionMode mode)
    {
        var source = """
            let proto = { nested: { value: 42 } };
            let obj = Object.create(proto);
            console.log(obj.nested.value);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\n", output);
    }

    // Object.create multiple objects from same prototype
    [Theory, ModeData]
    public void Object_Create_MultipleObjectsFromSamePrototype(ExecutionMode mode)
    {
        var source = """
            let proto = { x: 1 };
            let obj1 = Object.create(proto);
            let obj2 = Object.create(proto);
            obj1.x = 10;
            obj2.x = 20;
            console.log(proto.x);
            console.log(obj1.x);
            console.log(obj2.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("1\n10\n20\n", output);
    }

    // Object.create with method in prototype
    [Theory, ModeData]
    public void Object_Create_WithMethodInPrototype(ExecutionMode mode)
    {
        var source = """
            let proto = {
                value: 5,
                double() {
                    return this.value * 2;
                }
            };
            let obj = Object.create(proto);
            console.log(obj.double());
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    // Object.create - properties object overrides prototype properties
    [Theory, ModeData]
    public void Object_Create_PropertiesOverridePrototype(ExecutionMode mode)
    {
        var source = """
            let proto = { x: 1 };
            let obj = Object.create(proto, {
                x: { value: 100, writable: true, enumerable: true, configurable: true }
            });
            console.log(obj.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("100\n", output);
    }

    // Object.create with class instance as prototype
    [Theory, ModeData]
    public void Object_Create_WithClassInstancePrototype(ExecutionMode mode)
    {
        var source = """
            class Point {
                x: number;
                y: number;
                constructor(x: number, y: number) {
                    this.x = x;
                    this.y = y;
                }
            }
            let proto = new Point(10, 20);
            let obj = Object.create(proto);
            console.log(obj.x);
            console.log(obj.y);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n20\n", output);
    }

    // Object.create with array values in prototype
    [Theory, ModeData]
    public void Object_Create_WithArrayInPrototype(ExecutionMode mode)
    {
        var source = """
            let proto = { items: [1, 2, 3] };
            let obj = Object.create(proto);
            console.log(obj.items.length);
            console.log(obj.items[1]);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("3\n2\n", output);
    }

    // Object.create - verify Object.keys works on created object
    [Theory, ModeData]
    public void Object_Create_ObjectKeysWorks(ExecutionMode mode)
    {
        var source = """
            let proto = { a: 1, b: 2 };
            let obj = Object.create(proto);
            // ECMA-262 §20.1.2.16 Object.keys: own enumerable keys only.
            // Object.create(proto) returns a FRESH empty object with [[Prototype]]
            // = proto. proto's keys are reached via the prototype chain at
            // property-access time — they are NOT own keys of the created obj.
            let keys = Object.keys(obj);
            console.log(keys.length);
            // Inherited access still works through the prototype chain.
            console.log(obj.a);
            console.log(obj.b);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("0\n1\n2\n", output);
    }

    // Object.create with undefined second argument (same as not passing it)
    [Theory, ModeData]
    public void Object_Create_UndefinedPropertiesObject(ExecutionMode mode)
    {
        var source = """
            let proto = { x: 10 };
            let obj = Object.create(proto, undefined);
            console.log(obj.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("10\n", output);
    }

    // Object.create with mixed property types
    [Theory, ModeData]
    public void Object_Create_MixedPropertyTypes(ExecutionMode mode)
    {
        var source = """
            let proto = {
                num: 42,
                str: "hello",
                bool: true,
                arr: [1, 2],
                nested: { x: 1 }
            };
            let obj = Object.create(proto);
            console.log(obj.num);
            console.log(obj.str);
            console.log(obj.bool);
            console.log(obj.arr.length);
            console.log(obj.nested.x);
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal("42\nhello\ntrue\n2\n1\n", output);
    }

    // ECMA-262 §20.1.2.2 step 1: prototype must be Object or null (#104).
    [Theory, ModeData]
    public void Object_Create_NonObjectPrototype_ThrowsTypeError(ExecutionMode mode)
    {
        var source = """
            function attempt(label: string, fn: () => void) {
                try { fn(); console.log(label, "no throw"); }
                catch (e: any) { console.log(label, e instanceof TypeError); }
            }
            attempt("undefined", () => Object.create(undefined as any));
            attempt("number", () => Object.create(5 as any));
            attempt("string", () => Object.create("x" as any));
            attempt("bool", () => Object.create(true as any));
            // null and objects are valid prototypes — must NOT throw.
            console.log("null", typeof Object.create(null));
            console.log("obj", typeof Object.create({}));
            """;

        var output = TestHarness.Run(source, mode);
        Assert.Equal(
            "undefined true\nnumber true\nstring true\nbool true\nnull object\nobj object\n",
            output);
    }
}
