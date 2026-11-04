using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

public sealed class P3ReflectArrayLikeUnionTests
{
    [Fact]
    public void UnionOfArrayLikeArgumentObjectsIsAccepted()
        => Assert.Empty(P3ReflectAmbientTests.CheckProgram("""
            function read(...values:any[]) { return values.join(','); }
            function invoke(args:number[] | Uint8Array | String) {
                return Reflect.apply(read, null, args);
            }
            """));
}
