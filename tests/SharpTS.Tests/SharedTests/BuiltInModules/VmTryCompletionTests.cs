using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests.BuiltInModules;

public sealed class VmTryCompletionTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return ["try { 1; } catch (error) { 2; }", "1\n"];
        yield return ["try { throw 7; } catch (error) { 8; }", "8\n"];
        yield return ["try { 1; } finally { 2; }", "1\n"];
        yield return ["try { throw 7; } catch (error) { 8; } finally { 9; }", "8\n"];
        yield return ["try { { 3; } } catch (error) { 4; }", "3\n"];
        yield return ["try { throw 7; } catch (error) { try { error + 2; } finally { 3; } }", "9\n"];
    }

    public static IEnumerable<object[]> Modes() => Cases().SelectMany(row =>
        new[] { ExecutionMode.Interpreted, ExecutionMode.Compiled }.Select(mode =>
            new object[] { row[0], row[1], mode }));

    [Theory, MemberData(nameof(Modes))]
    public void KeepsTheActualTryOrCatchCompletion(string code, string expected, ExecutionMode mode)
    {
        string source = "import * as vm from 'node:vm';console.log(vm.runInNewContext(" +
            System.Text.Json.JsonSerializer.Serialize(code) + "));";
        Assert.Equal(expected, TestHarness.RunModules(new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", mode));
    }
}
