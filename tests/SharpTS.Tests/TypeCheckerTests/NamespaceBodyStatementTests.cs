using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.TypeCheckerTests;

/// <summary>
/// A namespace/module body may contain more than the fixed set of declarations: nested
/// <c>module</c> blocks, ambient <c>declare</c> declarations, and ordinary statements such as
/// assignment expression statements. (Conformance gap exposed once <c>module Foo {}</c> parsed.)
/// </summary>
public class NamespaceBodyStatementTests
{
    [Fact]
    public void ExpressionStatement_InModuleBody_Parses()
    {
        TestHarness.RunInterpreted("module M { export let x = 1; x = 2; }");
    }

    [Fact]
    public void NestedModule_Parses()
    {
        TestHarness.RunInterpreted("module Outer { export module Inner { export const v = 5; } }");
    }

    [Fact]
    public void DeclareInModuleBody_Parses()
    {
        TestHarness.RunInterpreted("module M { declare function f(): number; }");
    }

    [Fact]
    public void ModuleKeywordNestedInNamespace_Parses()
    {
        TestHarness.RunInterpreted("namespace N { module M { export const a = 1; } }");
    }
}
