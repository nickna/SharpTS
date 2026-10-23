using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class DeletedSynchronousFunctionMetadataTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["minimal-deleted-length", """
            function original(first: any, second: any) {}
            const target: any = original;
            console.log(delete target.length, Object.hasOwn(target, "length"), target.length, Reflect.get(target, "length"));
            """ + "\n", "true false 0 0\n"];
        yield return ["read-delete-matrix", """
            function dot(first: any, second: any) {}
            function index(first: any, second: any) {}
            function reflected(first: any, second: any) {}
            const dotted: any = dot, indexed: any = index, reflective: any = reflected;
            console.log(delete dotted.length, dotted.length, dotted["length"], Reflect.get(dotted, "length"), Object.hasOwn(dotted, "length"));
            console.log(delete indexed["length"], indexed.length, indexed["length"], Reflect.get(indexed, "length"), Object.hasOwn(indexed, "length"));
            console.log(Reflect.deleteProperty(reflective, "length"), reflective.length, reflective["length"], Reflect.get(reflective, "length"), Object.hasOwn(reflective, "length"));
            """ + "\n", "true 0 0 0 false\ntrue 0 0 0 false\ntrue 0 0 0 false\n"];
        yield return ["named-expression", """
            const target: any = function named(first: any, second: any) {};
            console.log(Reflect.deleteProperty(target, "name"), target.name === "", target["name"] === "", Reflect.get(target, "name") === "", Object.hasOwn(target, "name"));
            """ + "\n", "true true true true false\n"];
        yield return ["named-expression-value", """
            const target: any = function named(first: any, second: any) {};
            Reflect.deleteProperty(target, "name");
            console.log(target.name === undefined, target.name === "named", target.name === "", target["name"] === "", Reflect.get(target, "name") === "");
            """ + "\n", "false false true true true\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void DeletedMetadataUsesOrdinaryInheritedReads(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        new TypeChecker(TypeCheckerOptions.Strict).Check(TestHarness.ParseOrThrow(source));
        if (mode == ExecutionMode.Compiled)
        {
            var (errors, output) = TestHarness.CompileVerifyAndRun(source);
            Assert.Empty(errors);
            Assert.Equal(expected, output);
        }
        else
        {
            Assert.Equal(expected, TestHarness.RunInterpreted(source));
        }
    }
}
