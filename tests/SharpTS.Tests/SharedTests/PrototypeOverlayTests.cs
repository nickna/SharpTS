using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class PrototypeOverlayTests
{
    [Theory, ModeData]
    public void BuiltInDeletion_DefinitionAndAssignment_DoNotResurrectOriginal(ExecutionMode mode)
    {
        var source = """
            const prototypes: any[] = [Object.prototype, Array.prototype, String.prototype,
                Number.prototype, Boolean.prototype, BigInt.prototype, Symbol.prototype,
                Function.prototype, Error.prototype, Promise.prototype];
            const names = ["valueOf", "map", "trim", "toFixed", "valueOf", "valueOf",
                "valueOf", "bind", "toString", "then"];
            for (let i = 0; i < prototypes.length; i++) {
                const p: any = prototypes[i];
                const name = names[i];
                console.log(delete p[name]);
                console.log(Object.prototype.hasOwnProperty.call(p, name));
                Object.defineProperty(p, name, {
                    value: 17, writable: true, enumerable: false, configurable: true
                });
                console.log(p[name]);
                console.log(delete p[name]);
                console.log(Object.prototype.hasOwnProperty.call(p, name));
                p[name] = 23;
                console.log(p[name]);
                Object.defineProperty(p, name, { value: 23, writable: false, configurable: false });
                console.log(delete p[name]);
                let rejected = false;
                try { Object.defineProperty(p, name, { value: 99 }); }
                catch (e) { rejected = true; }
                console.log(rejected);
                console.log(p[name]);
            }
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat("true\nfalse\n17\ntrue\nfalse\n23\nfalse\ntrue\n23\n", 10)),
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void ExtraDescriptors_PreserveKeyOrderAttributesAndUndefinedAccessors(ExecutionMode mode)
    {
        var source = """
            function overlayKeys(p: any): string {
                const keys: string[] = [];
                for (const key in p) {
                    if (key === "2" || key === "9" || key.startsWith("overlay")) keys.push(key);
                }
                return keys.join(",");
            }
            const prototypes: any[] = [Array.prototype, String.prototype, Number.prototype,
                Boolean.prototype, BigInt.prototype, Symbol.prototype, Function.prototype,
                Error.prototype, Promise.prototype, Object.prototype];
            for (const p of prototypes) {
                p.overlayFirst = null;
                p[9] = 9;
                p[2] = 2;
                Object.defineProperty(p, "overlayHidden", { value: 1, configurable: true });
                Object.defineProperty(p, "overlayEmpty", {
                    get: undefined, set: undefined, enumerable: true, configurable: true
                });
                console.log(Object.getOwnPropertyDescriptor(p, "overlayFirst").value === null);
                console.log(p.overlayEmpty === undefined);
                console.log(Object.getOwnPropertyDescriptor(p, "overlayEmpty") !== undefined);
                const d = Object.getOwnPropertyDescriptor(p, "overlayHidden");
                console.log(d.writable, d.enumerable, d.configurable);
                console.log(overlayKeys(p));
                delete p.overlayFirst;
                p.overlayFirst = undefined;
                console.log(overlayKeys(p));
                delete p.overlayFirst;
                delete p[9];
                delete p[2];
                delete p.overlayHidden;
                delete p.overlayEmpty;
            }
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat(
            "true\ntrue\ntrue\nfalse false true\n2,9,overlayFirst,overlayEmpty\n2,9,overlayEmpty,overlayFirst\n", 10)),
            TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void AccessorDescriptors_PreserveIdentityWithoutInvokingCallbacks(ExecutionMode mode)
    {
        var source = """
            const prototypes: any[] = [Array.prototype, String.prototype, Number.prototype,
                Boolean.prototype, BigInt.prototype, Symbol.prototype, Function.prototype,
                Error.prototype, Object.prototype];
            for (const p of prototypes) {
                let reads = 0;
                let writes = 0;
                const getter = function(this: any): any { reads++; return this; };
                const setter = function(this: any, value: any): void { writes++; };
                Object.defineProperty(p, "overlayAccessor", {
                    get: getter, set: setter, enumerable: true, configurable: true
                });
                const d = Object.getOwnPropertyDescriptor(p, "overlayAccessor");
                console.log(d.get === getter, d.set === setter, reads, writes);
                delete p.overlayAccessor;
            }
            """;
        Assert.Equal(string.Concat(Enumerable.Repeat(
            "true true 0 0\n", 9)), TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void InheritedPrimitiveAccessors_PreserveExistingReceiverIdentity(ExecutionMode mode)
    {
        const string source = """
            const values: any = new Number(1);
            let getReceiver: any;
            let setReceiver: any;
            let assigned: any;
            Object.defineProperty(Number.prototype, "overlayAccessor", {
                get: function(this: any): any { getReceiver = this; return 42; },
                set: function(this: any, value: any): void { setReceiver = this; assigned = value; },
                configurable: true
            });
            console.log(values.overlayAccessor);
            values.overlayAccessor = 17;
            console.log(getReceiver === values, setReceiver === values, assigned);
            console.log(Object.prototype.hasOwnProperty.call(values, "overlayAccessor"));
            delete (Number.prototype as any).overlayAccessor;
            """;
        // Baseline difference: compiled Number getters do not retain boxed receiver
        // identity. The interpreter uses the boxed instance for both accessors.
        Assert.Equal(mode == ExecutionMode.Interpreted
            ? "42\ntrue true 17\nfalse\n"
            : "42\nfalse true 17\nfalse\n", TestHarness.Run(source, mode));
    }
}
