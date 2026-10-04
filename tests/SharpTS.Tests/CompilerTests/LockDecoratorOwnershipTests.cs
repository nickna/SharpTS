using System.Reflection;
using SharpTS.Compilation;
using SharpTS.Modules;
using SharpTS.Parsing;
using SharpTS.Tests.Infrastructure;
using SharpTS.TypeSystem;
using Xunit;

namespace SharpTS.Tests.CompilerTests;

public sealed class LockDecoratorOwnershipTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ModuleOwnersKeepLocksInEitherImportOrder(bool reverseImports, bool namespaced)
    {
        string root = Path.Combine(Path.GetTempPath(), "lock_owners_" + Guid.NewGuid().ToString("N"));
        string entry = Path.Combine(root, "main.ts");
        var files = new Dictionary<string, string>
        {
            [Path.Combine(root, "left.ts")] = (namespaced ? "@Namespace(\"Owners.Left\")\n" : "") + """
                export class Counter {
                    @lock read(): number { return 1; }
                    @lock async readAsync(): Promise<number> { return this.read(); }
                }
                """,
            [Path.Combine(root, "right.ts")] = (namespaced ? "@Namespace(\"Owners.Right\")\n" : "") + """
                export class Counter {
                    @lock read(): number { return 2; }
                    @lock async readAsync(): Promise<number> { return this.read(); }
                }
                """,
            [entry] = reverseImports
                ? "import { Counter as R } from './right'; import { Counter as L } from './left'; console.log(new L().read(), new R().read());"
                : "import { Counter as L } from './left'; import { Counter as R } from './right'; console.log(new L().read(), new R().read());"
        };
        var resolver = new ModuleResolver(entry, files);
        var modules = resolver.GetModulesInOrder(resolver.LoadModule(entry, DecoratorMode.Stage3));
        var checker = new TypeChecker();
        checker.SetDecoratorMode(DecoratorMode.Stage3);
        var typeMap = checker.CheckModules(modules, resolver);
        var compiler = new ILCompiler("owners_" + Guid.NewGuid().ToString("N"));
        compiler.SetDecoratorMode(DecoratorMode.Stage3);
        compiler.CompileModules(modules, resolver, typeMap);
        var assembly = Assembly.Load(compiler.SaveToBytes());
        var owners = assembly.GetTypes().Where(type => type.Name.EndsWith("_Counter", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, owners.Length);
        foreach (var owner in owners)
        {
            Assert.Contains(owner.GetMethod("read")!.GetMethodBody()!.ExceptionHandlingClauses,
                clause => clause.Flags == ExceptionHandlingClauseOptions.Finally);
            var instance = Activator.CreateInstance(owner)!;
            var result = await (Task<object>)owner.GetMethod("readAsync")!.Invoke(instance, null)!;
            Assert.Equal(owner.Name.Contains("_left_", StringComparison.Ordinal) ? 1d : 2d, result);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LockStorageIsSeparatedByClassInstanceAndCompilation(bool namespaced)
    {
        string source = (namespaced ? "@Namespace(\"Owners\")\n" : "") + """
            class Left {
                @lock read(): number { return 1; }
                @lock async readAsync(): Promise<number> { return this.read(); }
                @lock static read(): number { return 2; }
                @lock static async readAsync(): Promise<number> { return Left.read(); }
            }
            class Right {
                @lock read(): number { return 3; }
                @lock async readAsync(): Promise<number> { return this.read(); }
                @lock static read(): number { return 4; }
                @lock static async readAsync(): Promise<number> { return Right.read(); }
            }
            class Plain { read(): number { return 5; } }
            console.log(new Left().read(), new Right().read(), Left.read(), Right.read());
            """;
        Assert.Empty(TestHarness.CompileAndVerifyOnly(source, DecoratorMode.Stage3));
        var (first, output) = TestHarness.CompileAndRun(source, DecoratorMode.Stage3);
        var (second, secondOutput) = TestHarness.CompileAndRun(source, DecoratorMode.Stage3);
        Assert.Equal("1 3 2 4\n", output);
        Assert.Equal(output, secondOutput);
        string prefix = namespaced ? "Owners." : "";
        var left = first.GetType(prefix + "Left")!;
        var right = first.GetType(prefix + "Right")!;
        var otherLeft = second.GetType(prefix + "Left")!;
        var plain = first.GetType(prefix + "Plain")!;
        foreach (var type in new[] { left, right, otherLeft })
        {
            foreach (var flags in new[] { BindingFlags.Public | BindingFlags.Instance, BindingFlags.Public | BindingFlags.Static })
                Assert.Contains(type.GetMethod("read", flags)!.GetMethodBody()!.ExceptionHandlingClauses,
                    clause => clause.Flags == ExceptionHandlingClauseOptions.Finally);
        }
        var leftInstance = Activator.CreateInstance(left)!;
        var anotherInstance = Activator.CreateInstance(left)!;
        var rightInstance = Activator.CreateInstance(right)!;
        Assert.Equal(1d, await (Task<object>)left.GetMethod("readAsync", BindingFlags.Public | BindingFlags.Instance)!
            .Invoke(leftInstance, null)!);
        Assert.Equal(2d, await (Task<object>)left.GetMethod("readAsync", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!);
        foreach (var name in new[] { "_syncLock", "_asyncLock", "_lockReentrancy" })
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var field = left.GetField(name, flags)!;
            Assert.True(field.IsInitOnly);
            Assert.NotNull(field.GetValue(leftInstance));
            Assert.NotSame(field.GetValue(leftInstance), field.GetValue(anotherInstance));
            Assert.NotSame(field.GetValue(leftInstance), right.GetField(name, flags)!.GetValue(rightInstance));
            Assert.Null(plain.GetField(name, flags));
        }
        foreach (var name in new[] { "_staticSyncLock", "_staticAsyncLock", "_staticLockReentrancy" })
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            var field = left.GetField(name, flags)!;
            Assert.True(field.IsInitOnly);
            var value = field.GetValue(null);
            Assert.NotNull(value);
            Assert.Same(value, field.GetValue(null));
            Assert.NotSame(value, right.GetField(name, flags)!.GetValue(null));
            Assert.NotSame(value, otherLeft.GetField(name, flags)!.GetValue(null));
            Assert.Null(plain.GetField(name, flags));
        }
    }
}
