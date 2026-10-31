using SharpTS.Tests.Infrastructure;
using Xunit;

namespace SharpTS.Tests.SharedTests;

public class P2NativeConstructorBindingRegressionTests
{
    [Fact]
    public void ImportedNativeBindingsConstructThroughAliasesAndPreserveLocalShadows()
    {
        const string source = """
            import { EventEmitter as ImportedEmitter } from 'events';
            import { Blob as ImportedBlob, File as ImportedFile } from 'buffer';
            class EventEmitter { kind = 'local'; }
            class Blob { kind = 'local blob'; }
            const localEmitter: any = new EventEmitter();
            const localBlob: any = new Blob();
            console.log(localEmitter.kind, localBlob.kind);
            const blob = new ImportedBlob(['abc']);
            const file = new ImportedFile(['x'], 'x.txt');
            console.log(blob.size, file.name);
            const emitter = new ImportedEmitter();
            let called = false;
            emitter.on('event', () => { called = true; });
            emitter.emit('event');
            console.log(called);
            """;
        Assert.Equal("local local blob\n3 x.txt\ntrue\n", TestHarness.RunModules(
            new Dictionary<string, string> { ["main.ts"] = source }, "main.ts", ExecutionMode.Interpreted));
    }
}
