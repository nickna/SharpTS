using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class GlobalArrayBufferExposureTests
{
    [Theory, ModeData]
    public void GlobalReadsAndOwnDescriptorShareTheCanonicalConstructor(ExecutionMode mode)
    {
        const string source = """
            const constructor:any=ArrayBuffer;
            const descriptor:any=Object.getOwnPropertyDescriptor(globalThis,'ArrayBuffer');
            console.log(descriptor===undefined?'absent':'own',
                descriptor!==undefined&&descriptor.value===constructor,
                Reflect.get(globalThis,'ArrayBuffer')===constructor,
                globalThis.ArrayBuffer===constructor,
                globalThis['ArrayBuffer']===constructor);
            """;
        Assert.Equal("own true true true true\n", TestHarness.Run(source, mode));
    }

    [Theory, ModeData]
    public void IntrinsicDescriptorsRetainDeletionAndExplicitOverlays(ExecutionMode mode)
    {
        const string source = """
            const constructor:any=ArrayBuffer;
            const original:any=Object.getOwnPropertyDescriptor(globalThis,'ArrayBuffer');
            console.log(original.writable,original.enumerable,original.configurable);
            delete (globalThis as any).ArrayBuffer;
            console.log(Reflect.get(globalThis,'ArrayBuffer')===undefined,
                Object.getOwnPropertyDescriptor(globalThis,'ArrayBuffer')===undefined);
            Object.defineProperty(globalThis,'ArrayBuffer',{value:constructor,writable:false,enumerable:true,configurable:true});
            const replacement:any=Object.getOwnPropertyDescriptor(globalThis,'ArrayBuffer');
            console.log(replacement.value===constructor,replacement.writable,replacement.enumerable,
                replacement.configurable,Reflect.get(globalThis,'ArrayBuffer')===constructor);
            """;
        Assert.Equal("true false true\ntrue true\ntrue false true true true\n", TestHarness.Run(source, mode));
    }
}
