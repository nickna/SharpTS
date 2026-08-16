using SharpTS.TypeSystem.Exceptions;
using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// Parser coverage for two TS-conformance gaps:
/// (1) <c>module Foo { ... }</c> — the older spelling of <c>namespace Foo { ... }</c>;
/// (2) constructor types <c>new (params) =&gt; ReturnType</c> in type position, modelled as an
///     object type with a construct signature.
/// </summary>
public class ModuleAndConstructorTypeTests
{
    // ---- module (namespace) blocks --------------------------------------

    [Fact]
    public void ModuleBlock_Parses()
    {
        TestHarness.RunInterpreted("module Foo { class A {} }");
    }

    [Fact]
    public void ModuleBlock_MembersAreAccessible()
    {
        var output = TestHarness.RunInterpreted("module M { export const x = 41; } console.log(M.x + 1);");
        Assert.Equal("42\n", output);
    }

    // ---- constructor types ----------------------------------------------

    [Fact]
    public void ConstructorType_Annotation_Parses()
    {
        TestHarness.RunInterpreted("let f: new (x: number) => object;");
    }

    [Fact]
    public void ConstructorType_Alias_Parses()
    {
        TestHarness.RunInterpreted("type C = new (x: number) => object; let c: C;");
    }

    [Fact]
    public void ConstructorType_AsInterfaceProperty_Parses()
    {
        TestHarness.RunInterpreted("interface T { f: new (x: number) => void; } let t: T;");
    }

    [Fact]
    public void ConstructorType_RejectsPlainFunction()
    {
        // A constructor type is constructable; a plain arrow function is not (via #122 modelling).
        Assert.ThrowsAny<TypeCheckException>(() =>
            TestHarness.RunInterpreted("let C: new (x: number) => object; C = (x: number) => ({});"));
    }
}
