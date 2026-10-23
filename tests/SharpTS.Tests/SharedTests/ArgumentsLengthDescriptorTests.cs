using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.SharedTests;

[Collection("CompilationService")]
public sealed class ArgumentsLengthDescriptorTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["arguments-length-descriptor-index", """
            function check(value: number) {
                const args: any = arguments;
                Object.defineProperty(args, 'length', { writable: false });
                console.log(Reflect.set(args, '1', 7), args.length, Object.hasOwn(args, '1'), args[1]);
            }
            check(1);
            """, "true 1 true 7\n"];
        yield return ["ordinary-length-descriptor-preservation", """
            function ordinary(first: number, second: number) {
                const args: any = arguments;
                const initial: any = Object.getOwnPropertyDescriptor(args, "length");
                console.log("initial", args.length, initial.value, initial.writable,
                    initial.enumerable, initial.configurable);
                Object.defineProperty(args, "length", { writable: false });
                const partial: any = Object.getOwnPropertyDescriptor(args, "length");
                console.log("partial", args.length, partial.value, partial.writable,
                    partial.enumerable, partial.configurable);
                const reflected = Reflect.set(args, "2", 30);
                console.log("index", reflected, args.length, args[2], Object.hasOwn(args, "2"));
                Object.defineProperty(args, "length", { value: 9 });
                const explicit: any = Object.getOwnPropertyDescriptor(args, "length");
                console.log("explicit", args.length, explicit.value, explicit.writable,
                    explicit.enumerable, explicit.configurable);
            }
            ordinary(1, 2);
            function strictUnmapped(value: number) {
                "use strict";
                const args: any = arguments;
                Object.defineProperty(args, "length", { writable: false });
                args[0] = 7;
                args[1] = 8;
                console.log("unmapped", value, args[0], args[1], args.length);
            }
            strictUnmapped(1);
            """ + "\n", "initial 2 2 true false true\npartial 2 2 false false true\nindex true 2 30 true\nexplicit 9 9 false false true\nunmapped 1 7 8 1\n"];
    }

    public static IEnumerable<object[]> ModeCases() => Cases()
        .SelectMany(row => new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }
            .Select(mode => new object[] { row[0], row[1], row[2], mode }));

    [Theory, MemberData(nameof(ModeCases))]
    public void ArgumentsLengthIsAnIndependentOrdinaryDataProperty(
        string name, string source, string expected, ExecutionMode mode)
    {
        Assert.NotEmpty(name);
        Assert.Empty(new TypeChecker(TypeCheckerOptions.Strict)
            .CheckWithRecovery(TestHarness.ParseOrThrow(source)).Diagnostics);
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
