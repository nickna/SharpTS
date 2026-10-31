using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public sealed class RuntimeParentDestructuredMethodTests
{
    [Theory, ModeData]
    public void ImportedParentWithDestructuredMethodRemainsConstructible(ExecutionMode mode)
    {
        var modules = new Dictionary<string, string>
        {
            ["base.ts"] = """
                export class NodeBase {
                    read({ value }: { value: number }) { return value; }
                }
                """,
            ["alias.ts"] = """
                import { NodeBase } from './base';
                const Parent: any = NodeBase;
                export class Alias extends Parent {}
                """,
            ["main.ts"] = """
                import { Alias } from './alias';
                const node: any = new Alias();
                console.log(node.read({ value: 7 }));
                """
        };
        Assert.Equal("7\n", TestHarness.RunModules(modules, "main.ts", mode));
    }
}
